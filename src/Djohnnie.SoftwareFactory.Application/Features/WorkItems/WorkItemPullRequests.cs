using System.Text;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// The pull request of a work item (the one its progress is about): its diff, its discussion and feedback for its agent:
/// Copilot, or the DGHSF agent worker when the worker opened it.
/// </summary>
public sealed class WorkItemPullRequests(IWorkItemStore store, GitHubApiClient gitHub, IAgentRunStore agentRuns, WorkItemWorkflow workflow, TimeProvider time)
{
    public const string CopilotMention = "@copilot";

    /// <summary>Reads the pull request (with its description), its files, its comments and its reviews from GitHub (five or more requests); nothing is stored.</summary>
    public async Task<PullRequestChanges> GetChangesAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var (item, number) = await PullRequestOfAsync(repositoryFullName, id, cancellationToken);
        var pullRequest = await gitHub.GetPullRequestAsync(repositoryFullName, number, cancellationToken);
        var files = await gitHub.GetPullRequestFilesAsync(repositoryFullName, number, cancellationToken);
        var conversation = await gitHub.GetIssueCommentsAsync(repositoryFullName, number, cancellationToken);
        var lineComments = await gitHub.GetPullRequestReviewCommentsAsync(repositoryFullName, number, cancellationToken);
        var reviews = await gitHub.GetPullRequestReviewsAsync(repositoryFullName, number, cancellationToken);

        var comments = conversation.Select(c => ToComment(c, onLine: false))
            .Concat(lineComments.Select(c => ToComment(c, onLine: true)))
            .OrderBy(c => c.CreatedAt)
            .ToList();

        return new(pullRequest.Number, pullRequest.HtmlUrl, pullRequest.Title, pullRequest.IsOpen, pullRequest.MergedAt is not null, pullRequest.Draft,
            pullRequest.User?.Login, pullRequest.Additions, pullRequest.Deletions,
            files.Select(f => new PullRequestFileChange(f.FileName, f.PreviousFileName, f.Status, f.Additions, f.Deletions, f.Patch, f.BlobUrl)).ToList(),
            pullRequest.ChangedFiles > files.Count && files.Count >= GitHubApiClient.MaxPullRequestFiles,
            comments,
            ReviewStatus(reviews, pullRequest.MergeableState),
            (await WorkerRunsOnAsync(item, number, cancellationToken)) is not null,
            string.IsNullOrWhiteSpace(pullRequest.Body) ? null : Description(pullRequest));
    }

    private static PullRequestComment Description(GitHubPullRequestDetails pullRequest)
    {
        var kind = AuthorKind(pullRequest.User, pullRequest.Body);
        return new(pullRequest.User?.Login, BodyOf(kind, pullRequest.Body), pullRequest.CreatedAt, pullRequest.HtmlUrl, AuthorKind: kind);
    }

    /// <summary>
    /// Who wrote a comment: the DGHSF agent worker posts as the account it acts for, so its header (or mark) tells it apart
    /// from that person; Copilot and other bots are GitHub accounts of the type <c>Bot</c>.
    /// </summary>
    public static PullRequestAuthorKind AuthorKind(GitHubOwner? author, string? body)
    {
        if (DghsfHeader.IsFromDghsf(body))
        {
            return PullRequestAuthorKind.Dghsf;
        }
        if (author?.IsCopilot == true)
        {
            return PullRequestAuthorKind.Copilot;
        }
        return string.Equals(author?.Type, "Bot", StringComparison.OrdinalIgnoreCase) ? PullRequestAuthorKind.Bot : PullRequestAuthorKind.Person;
    }

    private static string BodyOf(PullRequestAuthorKind kind, string? body)
    {
        var text = body ?? string.Empty;
        return kind == PullRequestAuthorKind.Dghsf ? DghsfHeader.Strip(text) : text;
    }

    /// <summary>
    /// Each reviewer's verdict the way GitHub counts it: their latest review that approves, requests changes or was dismissed;
    /// a later comment-only review doesn't change it. Reviewers who only commented are listed as <c>COMMENTED</c>.
    /// </summary>
    /// <param name="reviews">The pull request's reviews, oldest first.</param>
    public static PullRequestReviewStatus ReviewStatus(IReadOnlyList<GitHubPullRequestReview> reviews, string? mergeableState)
    {
        var reviewers = reviews
            .Where(r => r.User is not null && r.State is not null && r.State != "PENDING")
            .GroupBy(r => r.User!.Login, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.LastOrDefault(r => r.State != "COMMENTED") ?? g.Last())
            .Select(r => new PullRequestReviewer(r.User!.Login, r.State!, r.SubmittedAt, r.HtmlUrl, AuthorKind(r.User, r.Body)))
            .OrderByDescending(r => r.SubmittedAt)
            .ToList();

        var decision = reviewers.Any(r => r.State == "CHANGES_REQUESTED") ? PullRequestReviewDecision.ChangesRequested
            : reviewers.Any(r => r.State == "APPROVED") ? PullRequestReviewDecision.Approved
            : PullRequestReviewDecision.None;
        return new(decision, reviewers, mergeableState);
    }

    /// <summary>Merges the pull request as the signed-in user, which closes it. Refreshing the work item afterwards picks up its progress.</summary>
    public async Task<PullRequestMergeResult> MergeAsync(PullRequestMerge merge, CancellationToken cancellationToken = default)
    {
        var (_, number) = await PullRequestOfAsync(merge.RepositoryFullName, merge.Id, cancellationToken);
        var pullRequest = await gitHub.GetPullRequestAsync(merge.RepositoryFullName, number, cancellationToken);
        if (!pullRequest.IsOpen)
        {
            throw new DomainRuleException($"Pull request #{number} is already {(pullRequest.MergedAt is null ? "closed" : "merged")}.");
        }
        if (pullRequest.Draft)
        {
            throw new DomainRuleException($"Pull request #{number} is a draft. Mark it as ready for review on GitHub before merging it.");
        }

        try
        {
            var sha = await gitHub.MergePullRequestAsync(merge.RepositoryFullName, number, MergeMethodName(merge.Method), cancellationToken);
            return new(pullRequest.HtmlUrl, sha);
        }
        catch (GitHubApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.Conflict
                                                or System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // E.g. merge conflicts, a branch rule that requires an approval or checks, or a merge method the repository doesn't allow.
            throw new DomainRuleException($"GitHub didn't merge pull request #{number}: {ex.Message}");
        }
    }

    public static string MergeMethodName(PullRequestMergeMethod method)
    {
        return method switch
        {
            PullRequestMergeMethod.Merge => "merge",
            PullRequestMergeMethod.Squash => "squash",
            PullRequestMergeMethod.Rebase => "rebase",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
        };
    }

    /// <summary>
    /// A conversation comment, or a line comment with its thread: GitHub points every answer in a thread at the comment that
    /// started it (<c>in_reply_to_id</c>), so that is the thread's id.
    /// </summary>
    private static PullRequestComment ToComment(GitHubComment comment, bool onLine)
    {
        var kind = AuthorKind(comment.User, comment.Body);
        if (!onLine)
        {
            return new(comment.User?.Login, BodyOf(kind, comment.Body), comment.CreatedAt, comment.HtmlUrl, Id: comment.Id, AuthorKind: kind);
        }
        // The current lines while the comment is still on the diff, otherwise the ones it was written on (as the diff hunk is).
        var current = comment.Line is not null;
        return new(comment.User?.Login, BodyOf(kind, comment.Body), comment.CreatedAt, comment.HtmlUrl,
            comment.Path, current ? comment.Line : comment.OriginalLine,
            comment.Id, comment.InReplyToId ?? comment.Id, kind,
            comment.DiffHunk, current ? comment.StartLine : comment.OriginalStartLine);
    }

    /// <summary>
    /// Without line comments (and without requesting changes) the feedback becomes a comment in the pull request's
    /// conversation; otherwise one review with the line comments. For Copilot it mentions <c>@copilot</c> once, at the start,
    /// and a review's body repeats the line comments, so Copilot gets all feedback however it reads the review.
    /// </summary>
    /// <remarks>
    /// On a pull request the DGHSF agent worker opened, nothing mentions Copilot: the feedback is posted as it is, and a
    /// revision run of the worker starts that reads the pull request's discussion, so it also gets feedback written on GitHub.
    /// That is refused while the worker is still running, because that run wouldn't see the feedback. Before anything is
    /// posted, the item and every item above it are refreshed from their GitHub issues, because the worker reads them from the
    /// database; when that fails, nothing is posted. Copilot reads the issues on GitHub itself, so its feedback skips that.
    /// Feedback for Copilot is remembered on the item (<see cref="WorkItem.CopilotRevisionRequestedAt"/>), so the sync asks
    /// Copilot code review to review the pull request once Copilot finished the revision; the worker does that itself.
    /// </remarks>
    public async Task<PullRequestFeedbackResult> SendFeedbackAsync(PullRequestFeedback feedback, CancellationToken cancellationToken = default)
    {
        var (item, number) = await PullRequestOfAsync(feedback.RepositoryFullName, feedback.Id, cancellationToken);
        var workerRuns = await WorkerRunsOnAsync(item, number, cancellationToken);
        var agent = workerRuns is null ? "Copilot only works" : "the agent only works";
        var pullRequest = await gitHub.GetPullRequestAsync(feedback.RepositoryFullName, number, cancellationToken);
        if (!pullRequest.IsOpen)
        {
            throw new DomainRuleException($"Pull request #{number} is {(pullRequest.MergedAt is null ? "closed" : "merged")}; {agent} on open pull requests.");
        }
        if (workerRuns is null)
        {
            var posted = await PostAsync(feedback, number, mentionCopilot: true, cancellationToken);
            try
            {
                await store.SetCopilotRevisionRequestedAsync(feedback.RepositoryFullName, feedback.Id, time.GetUtcNow(), cancellationToken);
                return new(posted);
            }
            catch (PersistenceException)
            {
                return new(posted, Warning: "Your feedback was posted, but DGHSF couldn't remember it, so Copilot code review won't be asked to review Copilot's revision.");
            }
        }

        if (workerRuns.Any(r => r.IsActive))
        {
            throw new DomainRuleException("The DGHSF agent worker is still working on this item and wouldn't see new feedback. Send it when its run has ended, or cancel the run.");
        }
        WorkItem refreshed;
        try
        {
            refreshed = await workflow.RefreshWithAncestorsAsync(item, cancellationToken);
        }
        catch (DomainRuleException ex)
        {
            throw new DomainRuleException($"{ex.Message} Your feedback wasn't sent.");
        }
        if (refreshed.Status == WorkItemStatus.Done)
        {
            throw new DomainRuleException($"GitHub issue #{refreshed.IssueNumber} is closed, so the agent can no longer revise its pull request. Your feedback wasn't sent.");
        }
        var url = await PostAsync(feedback, number, mentionCopilot: false, cancellationToken);
        try
        {
            await workflow.StartRevisionRunAsync(feedback.RepositoryFullName, feedback.Id, refreshFirst: false, cancellationToken);
            return new(url, AgentRunStarted: true);
        }
        catch (DomainRuleException ex)
        {
            return new(url, Warning: $"Your feedback was posted, but the agent didn't start on it: {ex.Message}");
        }
    }

    private async Task<string> PostAsync(PullRequestFeedback feedback, int number, bool mentionCopilot, CancellationToken cancellationToken)
    {
        if (feedback.LineComments.Count == 0 && !feedback.RequestChanges)
        {
            var body = feedback.Body.Trim();
            return await gitHub.CreateIssueCommentAsync(feedback.RepositoryFullName, number, mentionCopilot ? Mention(body) : body, cancellationToken);
        }

        var comments = feedback.LineComments
            .Select(c => new GitHubReviewLineComment(c.Path, c.Line, c.Side == DiffSide.Left ? "LEFT" : "RIGHT", c.Body.Trim()))
            .ToList();
        try
        {
            return await gitHub.CreatePullRequestReviewAsync(feedback.RepositoryFullName, number, mentionCopilot ? ReviewBody(feedback) : WorkerReviewBody(feedback),
                comments, feedback.RequestChanges, cancellationToken);
        }
        catch (GitHubApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            throw new DomainRuleException($"GitHub didn't accept the review; the pull request may have changed since you loaded it, so reload the changes and try again. ({ex.Message})");
        }
    }

    /// <summary>The feedback with <c>@copilot</c> in front, unless it already starts with it.</summary>
    public static string Mention(string text)
    {
        return text.StartsWith(CopilotMention, StringComparison.OrdinalIgnoreCase) ? text : $"{CopilotMention} {text}".TrimEnd();
    }

    /// <summary>
    /// A review's body for the DGHSF agent worker: the general feedback as it is. The worker reads every line comment
    /// itself, so they aren't repeated; GitHub needs some text to request changes.
    /// </summary>
    public static string WorkerReviewBody(PullRequestFeedback feedback)
    {
        var body = feedback.Body.Trim();
        return body.Length == 0 && feedback.RequestChanges ? "Please address the comments on the lines." : body;
    }

    public static string ReviewBody(PullRequestFeedback feedback)
    {
        var body = new StringBuilder(Mention(feedback.Body.Trim()));
        if (feedback.LineComments.Count > 0)
        {
            body.AppendLine().AppendLine();
            body.AppendLine(string.IsNullOrWhiteSpace(feedback.Body)
                ? "Please address the comments on these lines:"
                : "Please also address the comments on these lines:");
            foreach (var comment in feedback.LineComments)
            {
                var side = comment.Side == DiffSide.Left ? " (removed line)" : string.Empty;
                body.AppendLine($"- `{comment.Path}` line {comment.Line}{side}: {comment.Body.Trim().ReplaceLineEndings(" ")}");
            }
        }
        return body.ToString().TrimEnd();
    }

    private async Task<(WorkItem Item, int Number)> PullRequestOfAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
        var agent = item.Executor == AgentExecutor.DghsfWorker ? "the agent" : "Copilot";
        return (item, item.PullRequestNumber
            ?? throw new DomainRuleException($"This {item.Type.DisplayName.ToLowerInvariant()} has no pull request yet. Refresh it from GitHub once {agent} opened one."));
    }

    /// <summary>The item's runs of the DGHSF agent worker when the worker opened pull request <paramref name="number"/>; otherwise <c>null</c>.</summary>
    private async Task<IReadOnlyList<AgentRun>?> WorkerRunsOnAsync(WorkItem item, int number, CancellationToken cancellationToken)
    {
        if (item.Executor != AgentExecutor.DghsfWorker)
        {
            return null;
        }
        var runs = await agentRuns.GetForWorkItemAsync(item.RepositoryFullName, item.Id, cancellationToken);
        // A task keeps the pull request it opened, also while a later run on it is going on or after one failed.
        return runs.Any(r => r.PullRequestNumber == number) ? runs : null;
    }
}
