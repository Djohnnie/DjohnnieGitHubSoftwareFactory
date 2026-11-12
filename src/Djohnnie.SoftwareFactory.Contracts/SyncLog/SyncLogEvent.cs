namespace Djohnnie.SoftwareFactory.Contracts.SyncLog;

/// <summary>Something the work item sync did, as shown on the sync log page.</summary>
/// <param name="OccurredAt">When it happened; for an event shared by several runs, when the first of them ran.</param>
/// <param name="LastOccurredAt">When the last run of the event ran; the same as <paramref name="OccurredAt"/> for a single run.</param>
/// <param name="Iterations">The number of consecutive runs the event stands for; 1 except for the kinds that collapse.</param>
/// <param name="RepositoryFullName"><c>null</c> for an event about the whole run.</param>
/// <param name="IssueNumber">The work item's GitHub issue, for an event about one work item.</param>
/// <param name="Detail">More about a problem, e.g. the type of an unexpected error.</param>
public sealed record SyncLogEvent(
    Guid Id,
    Guid RunId,
    DateTimeOffset OccurredAt,
    DateTimeOffset LastOccurredAt,
    int Iterations,
    SyncLogLevel Level,
    SyncLogEventKind Kind,
    string? RepositoryFullName,
    int? IssueNumber,
    Guid? WorkItemId,
    string Message,
    string? Detail)
{
    public const int MaxMessageLength = 2000;
    public const int MaxDetailLength = 4000;

    public string? IssueUrl => RepositoryFullName is not null && IssueNumber is { } number
        ? $"https://github.com/{RepositoryFullName}/issues/{number}"
        : null;
}
