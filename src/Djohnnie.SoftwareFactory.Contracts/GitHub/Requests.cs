namespace Djohnnie.SoftwareFactory.Contracts.GitHub;

public sealed record RepositoryPathRequest(string RepositoryFullName, string Path);
public sealed record PutFileRequest(string RepositoryFullName, string Path, string Content, string Message, string? Sha);
public sealed record DeleteFileRequest(string RepositoryFullName, string Path, string Message, string Sha);
public sealed record UpdateCopilotCodeReviewSettingsRequest(string RepositoryFullName, CopilotCodeReviewSettingsUpdate Changes);

/// <summary>One page of a repository's workflow runs, newest first.</summary>
/// <param name="WorkflowId">Only the runs of this workflow; <c>null</c> for the runs of all workflows.</param>
/// <param name="Page">Zero-based.</param>
public sealed record WorkflowRunsRequest(string RepositoryFullName, long? WorkflowId, int Page, int PageSize)
{
    /// <summary>GitHub returns at most 100 runs per page.</summary>
    public const int MaxPageSize = 100;
}
