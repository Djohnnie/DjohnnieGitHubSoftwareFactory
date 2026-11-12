namespace Djohnnie.SoftwareFactory.Contracts.SyncLog;

/// <summary>A page of the sync log, newest first.</summary>
/// <param name="Page">Zero-based.</param>
/// <param name="MinimumLevel">Only events of this level and more serious ones.</param>
/// <param name="RepositoryFullName">Only the events about this repository; <c>null</c> for all events.</param>
public sealed record SyncLogQuery(int Page, int PageSize, SyncLogLevel MinimumLevel = SyncLogLevel.Information, string? RepositoryFullName = null)
{
    public const int MaxPageSize = 100;
}
