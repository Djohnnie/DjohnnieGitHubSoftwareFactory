using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>
/// Runs one started agent run: clone the base branch, let the agent implement the work item on a new branch, commit and push
/// its changes, open the pull request that closes the issue, and store the result with what the agent cost.
/// </summary>
/// <remarks>
/// While the agent works, the run's lease is renewed every third of <see cref="AgentRunOptions.Lease"/>; a renewal that finds
/// the run no longer running (cancelled in DGHSF) stops the agent and nothing is pushed. A failure before the agent started is
/// retried while the run has attempts left; after that it isn't, because a retry would pay for the agent again.
/// </remarks>
public sealed class AgentRunExecutor(
    IAgentRunStore runs,
    IWorkItemStore workItems,
    GitHubApiClient gitHub,
    IGitWorkspace git,
    ICodingAgent agent,
    WorkItemService workItemService,
    IOptions<AgentRunOptions> runOptions,
    IOptions<AgentWorkerOptions> workerOptions,
    ILogger<AgentRunExecutor> logger)
{
    public async Task<AgentRunStatus?> ExecuteAsync(AgentRun run, CancellationToken cancellationToken)
    {
        var settings = workerOptions.Value;
        var folder = Path.Combine(settings.EffectiveWorkDirectory, run.Id.ToString("N"));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cancelled = false;
        var heartbeat = KeepLeaseAsync();
        var agentStarted = false;
        AgentRunUsage? usage = null;
        try
        {
            var item = await workItems.GetAsync(run.RepositoryFullName, run.WorkItemId, stop.Token)
                ?? throw new DomainRuleException("The work item no longer exists.");
            if (item.IssueNumber is null || item.Status == WorkItemStatus.Done)
            {
                throw new DomainRuleException("The work item has no open GitHub issue to implement.");
            }
            var ancestors = item.ParentId is { } parentId
                ? await workItemService.GetAncestorsAsync(run.RepositoryFullName, parentId, stop.Token)
                : [];
            var baseBranch = item.BaseBranch ?? (await gitHub.GetRepositoryAsync(run.RepositoryFullName, stop.Token)).DefaultBranch ?? "main";
            var branch = AgentRunPrompt.BranchName(item, run.Id);
            var repository = Path.Combine(folder, "repo");

            logger.LogInformation("Run {Run}: cloning {Repository} ({Base}) for issue #{Issue}.", run.Id, run.RepositoryFullName, baseBranch, item.IssueNumber);
            await git.CloneAsync(run.RepositoryFullName, baseBranch, branch, repository, settings.GitHubToken, stop.Token);
            var profile = ReadCustomAgent(repository, item.CustomAgent);
            await RefreshQuietlyAsync(item);

            agentStarted = true;
            var outcome = await agent.RunAsync(new CodingAgentRequest(
                repository,
                Path.Combine(folder, "copilot"),
                AgentRunPrompt.Build(item, ancestors, branch, baseBranch),
                AgentRunPrompt.Instructions,
                item.Model,
                profile,
                settings.MaxAiCreditsPerRun,
                settings.RunTimeout), stop.Token);
            usage = outcome.Usage;
            logger.LogInformation("Run {Run}: the agent finished ({NanoAiu} nano AIU){Stopped}.", run.Id, usage.NanoAiu,
                outcome.StoppedEarly is null ? string.Empty : $": {outcome.StoppedEarly}");

            if (!await git.HasChangesAsync(repository, baseBranch, stop.Token))
            {
                var reason = outcome.StoppedEarly ?? "The agent finished without changing any files.";
                return await FailAsync(string.IsNullOrWhiteSpace(outcome.Summary) ? reason : $"{reason} It said: {outcome.Summary.Trim()}", retry: false);
            }

            await git.CommitAllAsync(repository, AgentRunPrompt.CommitMessage(item, run.Id), settings.CommitAuthorName, settings.CommitAuthorEmail, stop.Token);
            await git.PushAsync(repository, branch, settings.GitHubToken, stop.Token);
            var pullRequest = await gitHub.CreatePullRequestAsync(run.RepositoryFullName, item.Title.Trim(), branch, baseBranch,
                AgentRunPrompt.PullRequestBody(item, run.Id, outcome.Summary, usage, outcome.StoppedEarly),
                draft: settings.DraftPullRequests || outcome.StoppedEarly is not null, CancellationToken.None);
            logger.LogInformation("Run {Run}: opened pull request #{PullRequest}.", run.Id, pullRequest.Number);

            var completed = await runs.CompleteAsync(run.Id,
                new AgentRunResult(branch, pullRequest.Number, pullRequest.HtmlUrl, outcome.Summary, usage, outcome.StoppedEarly), CancellationToken.None);
            await RefreshQuietlyAsync(item);
            return completed ? AgentRunStatus.Succeeded : null;
        }
        catch (OperationCanceledException) when (cancelled)
        {
            logger.LogInformation("Run {Run} was cancelled; nothing was pushed.", run.Id);
            if (await workItems.GetAsync(run.RepositoryFullName, run.WorkItemId, CancellationToken.None) is { } item)
            {
                await RefreshQuietlyAsync(item);
            }
            return AgentRunStatus.Cancelled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The job is being stopped (e.g. its replica timeout): give the run back unless the agent already ran.
            return await FailAsync("The worker was stopped before the run finished.", retry: !agentStarted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {Run} failed.", run.Id);
            return await FailAsync(ex.Message, retry: !agentStarted && ex is not DomainRuleException);
        }
        finally
        {
            await stop.CancelAsync();
            await heartbeat;
            DeleteQuietly(folder);
        }

        async Task KeepLeaseAsync()
        {
            var lease = runOptions.Value.Lease;
            try
            {
                while (true)
                {
                    await Task.Delay(lease / 3, stop.Token);
                    if (!await runs.RenewLeaseAsync(run.Id, lease, stop.Token))
                    {
                        cancelled = true;
                        await stop.CancelAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The run ended.
            }
            catch (Exception ex)
            {
                // Without renewals the dispatcher takes the run back when the lease ends; the agent keeps working until then.
                logger.LogWarning(ex, "Could not renew the lease of run {Run}.", run.Id);
            }
        }

        async Task<AgentRunStatus?> FailAsync(string error, bool retry)
        {
            var status = await runs.FailAsync(run.Id, error, usage, retry, CancellationToken.None);
            if (await workItems.GetAsync(run.RepositoryFullName, run.WorkItemId, CancellationToken.None) is { } item)
            {
                await RefreshQuietlyAsync(item);
            }
            return status;
        }
    }

    /// <summary>The custom agent's profile from the clone, or <c>null</c> for the default agent.</summary>
    private static CodingAgentProfile? ReadCustomAgent(string repository, string? customAgent)
    {
        if (string.IsNullOrWhiteSpace(customAgent))
        {
            return null;
        }

        var path = Path.Combine(repository, AgentDefinition.GetPath(customAgent).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new DomainRuleException($"The custom agent '{customAgent}' doesn't exist on the base branch ({AgentDefinition.GetPath(customAgent)}).");
        }

        AgentDefinition definition;
        try
        {
            definition = AgentFileSerializer.Parse(File.ReadAllText(path));
        }
        catch (FormatException ex)
        {
            throw new DomainRuleException($"The custom agent '{customAgent}' can't be read: {ex.Message}");
        }
        // The tool names in agent files are GitHub's aliases, not the runtime's tool names, so every tool stays available.
        return new CodingAgentProfile(customAgent, definition.Name, definition.Description, definition.Prompt, definition.Model);
    }

    /// <summary>Updates the work item's status and progress from GitHub and the run; a failure only means the sync does it later.</summary>
    private async Task RefreshQuietlyAsync(WorkItem item)
    {
        try
        {
            await workItemService.RefreshAsync(item.RepositoryFullName, item.Id, CancellationToken.None);
        }
        catch (Exception ex) when (ex is GitHubApiException or HttpRequestException or DomainRuleException or PersistenceException)
        {
            logger.LogWarning(ex, "Could not refresh work item {Id}; the sync will.", item.Id);
        }
    }

    private void DeleteQuietly(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }
            // Git makes its object files read-only, which stops Directory.Delete on Windows.
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete {Folder}.", folder);
        }
    }
}
