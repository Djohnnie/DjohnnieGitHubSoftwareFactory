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
/// Runs one started agent run and stores the result with what the agent cost. An <see cref="AgentRunKind.Implement"/> run
/// clones the base branch, lets the agent implement the work item on a new branch, commits and pushes its changes and opens
/// the pull request that closes the issue. A <see cref="AgentRunKind.Revise"/> run clones the pull request's branch, merges
/// the base branch into it, lets the agent address the feedback written since its last run there, pushes to the same branch
/// and answers the feedback on the pull request.
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
    TimeProvider timeProvider,
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
        var progress = new RunProgress();
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

            var result = run.Kind == AgentRunKind.Revise
                ? await ReviseAsync(run, item, ancestors, folder, progress, stop.Token)
                : await ImplementAsync(run, item, ancestors, folder, progress, stop.Token);
            var completed = await runs.CompleteAsync(run.Id, result, CancellationToken.None);
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
            return await FailAsync("The worker was stopped before the run finished.", retry: !progress.AgentStarted);
        }
        catch (DomainRuleException ex)
        {
            logger.LogWarning("Run {Run} failed: {Reason}", run.Id, ex.Message);
            return await FailAsync(ex.Message, retry: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {Run} failed.", run.Id);
            return await FailAsync(ex.Message, retry: !progress.AgentStarted);
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
            var status = await runs.FailAsync(run.Id, error, progress.Usage, retry, CancellationToken.None);
            if (await workItems.GetAsync(run.RepositoryFullName, run.WorkItemId, CancellationToken.None) is { } item)
            {
                await RefreshQuietlyAsync(item);
            }
            return status;
        }
    }

    private async Task<AgentRunResult> ImplementAsync(AgentRun run, WorkItem item, IReadOnlyList<WorkItem> ancestors, string folder, RunProgress progress,
        CancellationToken cancellationToken)
    {
        var settings = workerOptions.Value;
        var baseBranch = item.BaseBranch ?? (await gitHub.GetRepositoryAsync(run.RepositoryFullName, cancellationToken)).DefaultBranch ?? "main";
        var branch = AgentRunPrompt.BranchName(item, run.Id);
        var repository = Path.Combine(folder, "repo");

        logger.LogInformation("Run {Run}: cloning {Repository} ({Base}) for issue #{Issue}.", run.Id, run.RepositoryFullName, baseBranch, item.IssueNumber);
        await git.CloneAsync(run.RepositoryFullName, baseBranch, branch, repository, settings.GitHubToken, cancellationToken);
        var profile = ReadCustomAgent(repository, item.CustomAgent);
        await RefreshQuietlyAsync(item);

        progress.AgentStarted = true;
        var outcome = await agent.RunAsync(new CodingAgentRequest(
            repository,
            Path.Combine(folder, "copilot"),
            AgentRunPrompt.Build(item, ancestors, branch, baseBranch),
            AgentRunPrompt.Instructions,
            item.Model,
            profile,
            settings.MaxAiCreditsPerRun,
            settings.RunTimeout), cancellationToken);
        var usage = progress.Usage = outcome.Usage;
        LogFinished(run, outcome);

        if (!await git.HasChangesAsync(repository, $"origin/{baseBranch}", cancellationToken))
        {
            var reason = outcome.StoppedEarly ?? "The agent finished without changing any files.";
            throw new DomainRuleException(string.IsNullOrWhiteSpace(outcome.Summary) ? reason : $"{reason} It said: {outcome.Summary.Trim()}");
        }

        await git.CommitAllAsync(repository, AgentRunPrompt.CommitMessage(item, run.Id), settings.CommitAuthorName, settings.CommitAuthorEmail, cancellationToken);
        await git.PushAsync(repository, branch, settings.GitHubToken, cancellationToken);
        var pullRequest = await gitHub.CreatePullRequestAsync(run.RepositoryFullName, item.Title.Trim(), branch, baseBranch,
            AgentRunPrompt.PullRequestBody(item, run.Id, outcome.Summary, usage, outcome.StoppedEarly),
            draft: settings.DraftPullRequests || outcome.StoppedEarly is not null, CancellationToken.None);
        logger.LogInformation("Run {Run}: opened pull request #{PullRequest}.", run.Id, pullRequest.Number);
        return new AgentRunResult(branch, pullRequest.Number, pullRequest.HtmlUrl, outcome.Summary, usage, outcome.StoppedEarly);
    }

    private async Task<AgentRunResult> ReviseAsync(AgentRun run, WorkItem item, IReadOnlyList<WorkItem> ancestors, string folder, RunProgress progress,
        CancellationToken cancellationToken)
    {
        var settings = workerOptions.Value;
        var number = run.PullRequestNumber ?? throw new DomainRuleException("The revision doesn't know which pull request to revise.");
        var pullRequest = await gitHub.GetPullRequestAsync(run.RepositoryFullName, number, cancellationToken);
        if (!pullRequest.IsOpen)
        {
            throw new DomainRuleException($"Pull request #{number} is {(pullRequest.MergedAt is null ? "closed" : "merged")}, so there is nothing to revise.");
        }
        var branch = pullRequest.Head?.Ref ?? run.Branch ?? throw new DomainRuleException($"The branch of pull request #{number} is unknown.");
        var baseBranch = pullRequest.Base?.Ref
            ?? item.BaseBranch
            ?? (await gitHub.GetRepositoryAsync(run.RepositoryFullName, cancellationToken)).DefaultBranch
            ?? "main";

        // Feedback written from now on is for the next revision, also when it arrives while this one runs.
        var until = timeProvider.GetUtcNow();
        var since = PullRequestDiscussion.HandledUntil(await runs.GetForWorkItemAsync(run.RepositoryFullName, run.WorkItemId, cancellationToken), run.Id, number);
        var discussion = await PullRequestDiscussion.LoadAsync(gitHub, run.RepositoryFullName, number, since, until, cancellationToken);
        if (discussion.Feedback.Count == 0)
        {
            throw new DomainRuleException($"There is no new feedback on pull request #{number} since the agent's last run on it.");
        }

        var repository = Path.Combine(folder, "repo");
        logger.LogInformation("Run {Run}: cloning {Repository} ({Branch}) to revise pull request #{PullRequest} with {Count} pieces of feedback.",
            run.Id, run.RepositoryFullName, branch, number, discussion.Feedback.Count);
        var head = await git.CloneBranchAsync(run.RepositoryFullName, branch, baseBranch, repository, settings.GitHubToken, cancellationToken);
        var merge = await git.MergeBaseAsync(repository, baseBranch, settings.CommitAuthorName, settings.CommitAuthorEmail, cancellationToken);
        var profile = ReadCustomAgent(repository, item.CustomAgent);
        await RefreshQuietlyAsync(item);

        var replies = new FeedbackReplies(discussion.Feedback);
        progress.AgentStarted = true;
        var outcome = await agent.RunAsync(new CodingAgentRequest(
            repository,
            Path.Combine(folder, "copilot"),
            AgentRunPrompt.BuildRevision(item, ancestors, number, pullRequest.Title, branch, baseBranch, discussion, merge),
            AgentRunPrompt.Instructions,
            item.Model,
            profile,
            settings.MaxAiCreditsPerRun,
            settings.RunTimeout,
            [replies.CreateTool()]), cancellationToken);
        var usage = progress.Usage = outcome.Usage;
        LogFinished(run, outcome);

        if (merge.Outcome == GitMergeOutcome.Conflicts && FilesWithConflictMarkers(repository, merge.Conflicts) is { Count: > 0 } unresolved)
        {
            throw new DomainRuleException($"The agent left merge conflicts with {baseBranch} in {string.Join(", ", unresolved)}, so nothing was pushed.");
        }

        // A revision may change nothing, e.g. when the feedback was a question; its answers are still posted.
        var pushed = await git.HasChangesAsync(repository, head, cancellationToken);
        if (pushed)
        {
            await git.CommitAllAsync(repository, AgentRunPrompt.RevisionCommitMessage(item, number, run.Id, baseBranch, merge.Outcome == GitMergeOutcome.Conflicts),
                settings.CommitAuthorName, settings.CommitAuthorEmail, cancellationToken);
            await git.PushAsync(repository, branch, settings.GitHubToken, cancellationToken);
            logger.LogInformation("Run {Run}: pushed the revision of pull request #{PullRequest}.", run.Id, number);
        }

        var problems = await AnswerAsync(run, number, discussion, replies, outcome, pushed, branch, baseBranch, merge);
        var warning = string.Join(" ", new[] { outcome.StoppedEarly }.Concat(problems).OfType<string>());
        return new AgentRunResult(branch, number, pullRequest.HtmlUrl, outcome.Summary, usage, warning.Length == 0 ? null : warning, until);
    }

    /// <summary>
    /// Replies to every line comment the agent answered, in its thread, and posts one comment with the summary and the other
    /// answers. The changes are pushed by then, so a failure here is only reported.
    /// </summary>
    /// <returns>What couldn't be posted.</returns>
    private async Task<List<string>> AnswerAsync(AgentRun run, int number, PullRequestDiscussion discussion, FeedbackReplies replies, CodingAgentOutcome outcome,
        bool pushed, string branch, string baseBranch, GitMergeResult merge)
    {
        var problems = new List<string>();
        foreach (var entry in discussion.Feedback.Where(f => f is { Kind: PullRequestDiscussionKind.LineComment, ThreadId: not null }))
        {
            if (replies.ReplyTo(entry.Id!) is not { } reply)
            {
                continue;
            }
            try
            {
                await gitHub.ReplyToReviewCommentAsync(run.RepositoryFullName, number, entry.ThreadId!.Value, AgentRunPrompt.ReplyBody(run.Id, reply), CancellationToken.None);
            }
            catch (Exception ex) when (ex is GitHubApiException or HttpRequestException)
            {
                logger.LogWarning(ex, "Run {Run}: could not answer {Comment}.", run.Id, entry.Url);
                problems.Add($"Could not answer {entry.Url}: {ex.Message}");
            }
        }

        try
        {
            await gitHub.CreateIssueCommentAsync(run.RepositoryFullName, number,
                AgentRunPrompt.RevisionComment(run.Id, outcome.Summary, outcome.Usage, outcome.StoppedEarly, discussion, replies, pushed, branch, baseBranch, merge),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is GitHubApiException or HttpRequestException)
        {
            logger.LogWarning(ex, "Run {Run}: could not comment on pull request #{PullRequest}.", run.Id, number);
            problems.Add($"Could not comment on pull request #{number}: {ex.Message}");
        }
        return problems;
    }

    /// <summary>The files that still have a conflict marker at the start of a line; files that were deleted have none.</summary>
    public static IReadOnlyList<string> FilesWithConflictMarkers(string repository, IEnumerable<string> files)
    {
        return files
            .Where(file =>
            {
                var path = Path.Combine(repository, file.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(path) && File.ReadLines(path).Any(line =>
                    line.StartsWith("<<<<<<< ", StringComparison.Ordinal) || line.StartsWith(">>>>>>> ", StringComparison.Ordinal));
            })
            .ToList();
    }

    private void LogFinished(AgentRun run, CodingAgentOutcome outcome)
    {
        logger.LogInformation("Run {Run}: the agent finished ({NanoAiu} nano AIU){Stopped}.", run.Id, outcome.Usage.NanoAiu,
            outcome.StoppedEarly is null ? string.Empty : $": {outcome.StoppedEarly}");
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
            throw new DomainRuleException($"The custom agent '{customAgent}' doesn't exist on the branch ({AgentDefinition.GetPath(customAgent)}).");
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

    /// <summary>How far a run got, for what happens when it fails.</summary>
    private sealed class RunProgress
    {
        /// <summary>Whether the agent started: a retry would pay for it again.</summary>
        public bool AgentStarted { get; set; }

        /// <summary>What the agent cost, once it finished.</summary>
        public AgentRunUsage? Usage { get; set; }
    }
}
