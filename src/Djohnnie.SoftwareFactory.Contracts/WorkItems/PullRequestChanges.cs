namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>The changes of a work item's pull request and the discussion on it, as GitHub has them now.</summary>
/// <param name="IsOpen">Whether feedback can still be sent (not merged or closed).</param>
/// <param name="FilesTruncated">GitHub lists at most 3,000 files of a pull request; there were more.</param>
/// <param name="OpenedByWorker">
/// The DGHSF agent worker opened it: feedback goes to the worker (a revision run) instead of to Copilot.
/// </param>
/// <param name="Description">
/// The pull request's description as its opening post; <c>null</c> when it has none. The DGHSF agent worker writes its summary
/// and screenshots there.
/// </param>
public sealed record PullRequestChanges(
    int Number,
    string Url,
    string Title,
    bool IsOpen,
    bool IsMerged,
    bool IsDraft,
    string? AuthorLogin,
    int Additions,
    int Deletions,
    IReadOnlyList<PullRequestFileChange> Files,
    bool FilesTruncated,
    IReadOnlyList<PullRequestComment> Comments,
    PullRequestReviewStatus Review,
    bool OpenedByWorker = false,
    PullRequestComment? Description = null);

/// <summary>Where the pull request stands with its reviewers and whether GitHub would merge it.</summary>
/// <param name="Reviewers">Each reviewer's verdict, most recent first.</param>
/// <param name="MergeState">GitHub's <c>mergeable_state</c>: <c>clean</c>, <c>blocked</c>, <c>behind</c>, <c>dirty</c>, <c>unstable</c>, <c>draft</c>, <c>has_hooks</c> or <c>unknown</c> (still being computed).</param>
public sealed record PullRequestReviewStatus(
    PullRequestReviewDecision Decision,
    IReadOnlyList<PullRequestReviewer> Reviewers,
    string? MergeState)
{
    public static PullRequestReviewStatus None { get; } = new(PullRequestReviewDecision.None, [], null);
}

public enum PullRequestReviewDecision
{
    /// <summary>Nobody approved or requested changes (yet).</summary>
    None = 0,

    /// <summary>At least one reviewer approved and nobody's latest verdict requests changes.</summary>
    Approved = 1,

    /// <summary>At least one reviewer's latest verdict requests changes.</summary>
    ChangesRequested = 2
}

/// <param name="State">The reviewer's verdict: <c>APPROVED</c>, <c>CHANGES_REQUESTED</c>, <c>DISMISSED</c>, or <c>COMMENTED</c> when they only commented.</param>
public sealed record PullRequestReviewer(string Login, string State, DateTimeOffset? SubmittedAt, string? Url,
    PullRequestAuthorKind Kind = PullRequestAuthorKind.Person);

/// <summary>
/// Merges a work item's pull request as the signed-in user, which also closes it. GitHub lets you merge a pull request you
/// opened yourself, even though it doesn't let you approve it.
/// </summary>
public sealed record PullRequestMerge(string RepositoryFullName, Guid Id, PullRequestMergeMethod Method = PullRequestMergeMethod.Merge);

/// <summary>How the pull request's commits get onto the base branch, as GitHub's merge button offers it.</summary>
public enum PullRequestMergeMethod
{
    /// <summary>Create a merge commit.</summary>
    Merge = 0,

    /// <summary>Squash the commits into one.</summary>
    Squash = 1,

    /// <summary>Rebase the commits onto the base branch.</summary>
    Rebase = 2
}

/// <param name="Url">The merged pull request on GitHub.</param>
/// <param name="Sha">The commit the merge created on the base branch.</param>
public sealed record PullRequestMergeResult(string Url, string? Sha);

/// <param name="Status"><c>added</c>, <c>removed</c>, <c>modified</c>, <c>renamed</c>, <c>copied</c>, <c>changed</c> or <c>unchanged</c>.</param>
/// <param name="Patch">The unified diff hunks; <c>null</c> for binary files and diffs GitHub considers too large.</param>
public sealed record PullRequestFileChange(
    string FileName,
    string? PreviousFileName,
    string Status,
    int Additions,
    int Deletions,
    string? Patch,
    string? BlobUrl);

/// <summary>A comment in the pull request's conversation, or on a line of its diff when <see cref="Path"/> is set.</summary>
/// <param name="AuthorLogin">The GitHub account that posted it; for <see cref="PullRequestAuthorKind.Dghsf"/> the account DGHSF acted for.</param>
/// <param name="Body">The Markdown; without DGHSF's header for <see cref="PullRequestAuthorKind.Dghsf"/>, which pages show as the author.</param>
/// <param name="ThreadId">For a line comment: the id of the comment that started its thread (its own id when it did).</param>
/// <param name="DiffHunk">For a line comment: the diff as it was when it was written, ending at <see cref="Line"/>, as GitHub shows it above the thread.</param>
/// <param name="StartLine">For a line comment on several lines: the first of them, up to <see cref="Line"/>.</param>
public sealed record PullRequestComment(
    string? AuthorLogin,
    string Body,
    DateTimeOffset CreatedAt,
    string Url,
    string? Path = null,
    int? Line = null,
    long? Id = null,
    long? ThreadId = null,
    PullRequestAuthorKind AuthorKind = PullRequestAuthorKind.Person,
    string? DiffHunk = null,
    int? StartLine = null)
{
    /// <summary>A line comment that answers an earlier one in its thread.</summary>
    public bool IsReply
    {
        get
        {
            return ThreadId is not null && ThreadId != Id;
        }
    }
}

/// <summary>Who wrote a comment, which GitHub's author alone doesn't tell: DGHSF posts as the account it acts for.</summary>
public enum PullRequestAuthorKind
{
    /// <summary>A person, e.g. you on GitHub or through DGHSF's feedback form.</summary>
    Person = 0,

    /// <summary>The DGHSF agent worker, on behalf of <see cref="PullRequestComment.AuthorLogin"/>.</summary>
    Dghsf = 1,

    /// <summary>The Copilot cloud agent or Copilot code review.</summary>
    Copilot = 2,

    /// <summary>Another bot or app, e.g. a CI integration.</summary>
    Bot = 3
}

/// <summary>
/// Feedback on a work item's pull request. For a pull request of Copilot, DGHSF adds the <c>@copilot</c> mention; for one
/// the DGHSF agent worker opened, it starts a revision run of the worker instead.
/// </summary>
/// <param name="Body">General feedback; may be empty when there are line comments.</param>
/// <param name="RequestChanges">Submit as a review that requests changes instead of a plain comment or review.</param>
public sealed record PullRequestFeedback(
    string RepositoryFullName,
    Guid Id,
    string Body,
    IReadOnlyList<PullRequestLineComment> LineComments,
    bool RequestChanges = false)
{
    /// <summary>GitHub's limit for a comment body is 65,536 characters; this leaves room for the mention and the line summary.</summary>
    public const int MaxBodyLength = 20_000;

    public const int MaxLineComments = 50;
}

/// <summary>Feedback on one line of the diff.</summary>
/// <param name="Line">The line number in the file: the new file for <see cref="DiffSide.Right"/>, the old one for <see cref="DiffSide.Left"/>.</param>
public sealed record PullRequestLineComment(string Path, int Line, DiffSide Side, string Body);

/// <summary>Which version of the file a diff line belongs to, as the pull request review API names it.</summary>
public enum DiffSide
{
    /// <summary>The new version: added and unchanged lines.</summary>
    Right = 0,

    /// <summary>The old version: removed lines.</summary>
    Left = 1
}

/// <param name="Url">The comment or review on GitHub.</param>
/// <param name="AgentRunStarted">The DGHSF agent worker will start a revision run on the feedback.</param>
/// <param name="Warning">Why the feedback was posted but no revision run started.</param>
public sealed record PullRequestFeedbackResult(string Url, bool AgentRunStarted = false, string? Warning = null);
