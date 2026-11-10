namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>The changes of a work item's pull request and the discussion on it, as GitHub has them now.</summary>
/// <param name="IsOpen">Whether feedback can still be sent (not merged or closed).</param>
/// <param name="FilesTruncated">GitHub lists at most 3,000 files of a pull request; there were more.</param>
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
    IReadOnlyList<PullRequestComment> Comments);

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
public sealed record PullRequestComment(
    string? AuthorLogin,
    string Body,
    DateTimeOffset CreatedAt,
    string Url,
    string? Path = null,
    int? Line = null);

/// <summary>Feedback for Copilot on a work item's pull request; DGHSF adds the <c>@copilot</c> mention.</summary>
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
public sealed record PullRequestFeedbackResult(string Url);
