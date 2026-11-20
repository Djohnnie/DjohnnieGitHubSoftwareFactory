using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Asks the item's agent to fix the merge conflicts of its pull request with a comment in the pull request's discussion, sent
/// like feedback (<see cref="WorkItemPullRequests.SendFeedbackAsync"/>): for the Copilot cloud agent it mentions
/// <c>@copilot</c>, which starts Copilot; for the DGHSF agent worker it starts a revision run, which merges the base branch
/// and reads the comment as its feedback. The sync does it by itself once per conflict for items with
/// <see cref="WorkItem.FixMergeConflicts"/>; the button on the pull request does it on request.
/// </summary>
public sealed class MergeConflictFixes(IWorkItemStore store, IAgentRunStore agentRuns, WorkItemPullRequests pullRequests, TimeProvider time)
{
    /// <summary>The comment that asks the agent to fix the conflicts.</summary>
    public const string Request =
        "This pull request has merge conflicts with its base branch. Please merge the latest base branch into this branch, resolve "
        + "every conflict so that what both sides meant is kept, and make sure the code still builds and the tests still pass.";

    /// <summary>
    /// Asks the agent when the item fixes conflicts by itself, its pull request conflicts as of the last refresh, the agent
    /// wasn't asked for this conflict yet and isn't working. Every conflict is acted on once
    /// (<see cref="WorkItem.MergeConflictFixRequestedAt"/>), also when the agent couldn't be asked.
    /// </summary>
    /// <exception cref="Application.Features.GitHub.GitHubApiException">GitHub refused the token, the rate limit was reached or GitHub had an error.</exception>
    public async Task<MergeConflictFixResult> RequestWhenConflictingAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
        if (item is not { FixMergeConflicts: true, PullRequestHasConflicts: true, Status: WorkItemStatus.InProgress, MergeConflictFixRequestedAt: null, PullRequestNumber: { } number }
            || item.Progress is CopilotProgress.Queued or CopilotProgress.Working or CopilotProgress.Merged or CopilotProgress.Closed)
        {
            return MergeConflictFixResult.NotDue;
        }
        // A running worker wouldn't see the request; its next run merges the base branch anyway, and if that leaves conflicts the next sync asks.
        if (item.Executor == AgentExecutor.DghsfWorker
            && (await agentRuns.GetForWorkItemAsync(repositoryFullName, id, cancellationToken)).Any(r => r.IsActive))
        {
            return MergeConflictFixResult.NotDue;
        }

        try
        {
            var posted = await pullRequests.SendFeedbackAsync(new PullRequestFeedback(repositoryFullName, id, Request, []), cancellationToken);
            await store.SetMergeConflictFixRequestedAsync(repositoryFullName, id, time.GetUtcNow(), cancellationToken);
            return new(MergeConflictFixOutcome.Requested, number, posted.AgentRunStarted, posted.Warning);
        }
        catch (DomainRuleException ex)
        {
            await store.SetMergeConflictFixRequestedAsync(repositoryFullName, id, time.GetUtcNow(), cancellationToken);
            return new(MergeConflictFixOutcome.Skipped, number, Reason: ex.Message);
        }
    }

    /// <summary>Asks the agent now, as the signed-in user; remembered, so the sync doesn't ask again for the same conflict.</summary>
    public async Task<PullRequestFeedbackResult> RequestAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var posted = await pullRequests.SendFeedbackAsync(new PullRequestFeedback(repositoryFullName, id, Request, []), cancellationToken);
        try
        {
            await store.SetMergeConflictFixRequestedAsync(repositoryFullName, id, time.GetUtcNow(), cancellationToken);
            return posted;
        }
        catch (PersistenceException)
        {
            var remembered = "DGHSF couldn't remember the request, so the sync may ask once more.";
            return posted with { Warning = posted.Warning is null ? remembered : $"{posted.Warning} {remembered}" };
        }
    }
}
