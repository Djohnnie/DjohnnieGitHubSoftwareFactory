using System.Globalization;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>Where on the pull request a piece of its discussion was written.</summary>
public enum PullRequestDiscussionKind
{
    /// <summary>A comment in the pull request's conversation.</summary>
    Comment = 0,

    /// <summary>The text of a submitted review.</summary>
    Review = 1,

    /// <summary>A comment on a line of the diff, in its own thread.</summary>
    LineComment = 2
}

/// <summary>One comment or review of a pull request's discussion.</summary>
/// <param name="Id">For new feedback: <c>F1</c>, <c>F2</c>, … in the order it was written, which the agent answers by; otherwise <c>null</c>.</param>
/// <param name="ThreadId">For a line comment: the first comment of its thread, which replies go to.</param>
public sealed record PullRequestDiscussionEntry(
    string? Id,
    PullRequestDiscussionKind Kind,
    string? Author,
    string Body,
    DateTimeOffset CreatedAt,
    string Url,
    string? Path = null,
    int? Line = null,
    long? ThreadId = null);

/// <summary>
/// A pull request's discussion as a revision run of the DGHSF agent worker sees it: the feedback written since the agent's
/// last run, and what was said before as context. What the worker itself posted carries <see cref="Marker"/> and is left out.
/// </summary>
public sealed record PullRequestDiscussion(IReadOnlyList<PullRequestDiscussionEntry> Feedback, IReadOnlyList<PullRequestDiscussionEntry> Earlier)
{
    /// <summary>How many earlier comments are kept as context, the most recent ones.</summary>
    public const int MaxEarlier = 40;

    public const int MaxFeedbackLength = 8_000;
    public const int MaxEarlierLength = 1_500;

    private const string MarkerPrefix = "<!-- dghsf:agent-run";

    /// <summary>The hidden mark on every comment the worker posts.</summary>
    public static string Marker(Guid runId)
    {
        return $"{MarkerPrefix}:{runId} -->";
    }

    public static bool IsFromWorker(string? body)
    {
        return body is not null && body.Contains(MarkerPrefix, StringComparison.Ordinal);
    }

    /// <summary>Reads the conversation, the line comments and the reviews of the pull request (three or more requests).</summary>
    /// <param name="since">Feedback is what was written after this time.</param>
    /// <param name="until">…and up to this time; what comes later is for the next revision.</param>
    public static async Task<PullRequestDiscussion> LoadAsync(GitHubApiClient gitHub, string repositoryFullName, int number, DateTimeOffset since, DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var conversation = await gitHub.GetIssueCommentsAsync(repositoryFullName, number, cancellationToken);
        var lineComments = await gitHub.GetPullRequestReviewCommentsAsync(repositoryFullName, number, cancellationToken);
        var reviews = await gitHub.GetPullRequestReviewsAsync(repositoryFullName, number, cancellationToken);
        return Split(conversation, lineComments, reviews, since, until);
    }

    /// <summary>Orders the discussion by time, leaves out the worker's own comments and empty reviews, and numbers the new feedback.</summary>
    public static PullRequestDiscussion Split(IEnumerable<GitHubComment> conversation, IEnumerable<GitHubComment> lineComments,
        IEnumerable<GitHubPullRequestReview> reviews, DateTimeOffset since, DateTimeOffset until)
    {
        var entries = conversation
            .Select(c => new PullRequestDiscussionEntry(null, PullRequestDiscussionKind.Comment, c.User?.Login, c.Body ?? string.Empty, c.CreatedAt, c.HtmlUrl))
            .Concat(lineComments.Select(c => new PullRequestDiscussionEntry(null, PullRequestDiscussionKind.LineComment, c.User?.Login, c.Body ?? string.Empty,
                c.CreatedAt, c.HtmlUrl, c.Path, c.Line ?? c.OriginalLine, c.InReplyToId ?? c.Id)))
            .Concat(reviews
                .Where(r => r.SubmittedAt is not null && r.State != "PENDING")
                .Select(r => new PullRequestDiscussionEntry(null, PullRequestDiscussionKind.Review, r.User?.Login, ReviewText(r), r.SubmittedAt!.Value, r.HtmlUrl ?? string.Empty)))
            .Where(e => !string.IsNullOrWhiteSpace(e.Body) && !IsFromWorker(e.Body))
            .OrderBy(e => e.CreatedAt)
            .ToList();

        var feedback = entries
            .Where(e => e.CreatedAt > since && e.CreatedAt <= until)
            .Select((e, index) => e with { Id = string.Create(CultureInfo.InvariantCulture, $"F{index + 1}"), Body = Shorten(e.Body, MaxFeedbackLength) })
            .ToList();
        var earlier = entries
            .Where(e => e.CreatedAt <= since)
            .TakeLast(MaxEarlier)
            .Select(e => e with { Body = Shorten(e.Body, MaxEarlierLength) })
            .ToList();
        return new(feedback, earlier);
    }

    /// <summary>
    /// Until when the pull request's discussion was handled: by the latest run on it that succeeded (the one that opened it
    /// or an earlier revision). A task keeps that time while a later run of it is going on or after one failed; tasks from
    /// before an item had one task only have it when they succeeded.
    /// </summary>
    public static DateTimeOffset HandledUntil(IEnumerable<AgentRun> runs, int pullRequestNumber)
    {
        return runs
            .Where(r => r.PullRequestNumber == pullRequestNumber && (r.FeedbackUntil is not null || r.Status == AgentRunStatus.Succeeded))
            .Select(r => r.FeedbackUntil ?? r.CompletedAt ?? DateTimeOffset.MinValue)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
    }

    /// <summary>
    /// A review's text, empty when it has none: its line comments are entries of their own, so a review without text has
    /// nothing to answer.
    /// </summary>
    private static string ReviewText(GitHubPullRequestReview review)
    {
        var body = review.Body?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            return body;
        }
        return review.State switch
        {
            "CHANGES_REQUESTED" => $"(Requested changes.) {body}",
            "APPROVED" => $"(Approved.) {body}",
            _ => body
        };
    }

    private static string Shorten(string text, int maxLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : $"{trimmed[..maxLength]}… (shortened)";
    }
}
