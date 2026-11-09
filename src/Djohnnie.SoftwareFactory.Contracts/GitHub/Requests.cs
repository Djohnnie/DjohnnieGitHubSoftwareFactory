namespace Djohnnie.SoftwareFactory.Contracts.GitHub;

public sealed record RepositoryPathRequest(string RepositoryFullName, string Path);
public sealed record PutFileRequest(string RepositoryFullName, string Path, string Content, string Message, string? Sha);
public sealed record DeleteFileRequest(string RepositoryFullName, string Path, string Message, string Sha);
public sealed record UpdateCopilotCodeReviewSettingsRequest(string RepositoryFullName, CopilotCodeReviewSettingsUpdate Changes);
