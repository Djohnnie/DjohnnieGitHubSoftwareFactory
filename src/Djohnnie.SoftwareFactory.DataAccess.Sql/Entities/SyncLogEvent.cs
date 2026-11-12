using System.ComponentModel.DataAnnotations;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// One event of the work item sync. Rows are kept forever and never refer to other tables: a work item or repository
/// that is deleted later keeps its events.
/// </summary>
public class SyncLogEvent : Entity
{
    /// <summary>The run that wrote the event; for an event shared by several runs, the last of them.</summary>
    public Guid RunId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset LastOccurredAt { get; set; }
    public int Iterations { get; set; } = 1;
    public SyncLogLevel Level { get; set; }
    public SyncLogEventKind Kind { get; set; }

    [MaxLength(Repository.MaxFullNameLength)]
    public string? RepositoryFullName { get; set; }

    public int? IssueNumber { get; set; }
    public Guid? WorkItemId { get; set; }

    [Required, MaxLength(Contracts.SyncLog.SyncLogEvent.MaxMessageLength)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(Contracts.SyncLog.SyncLogEvent.MaxDetailLength)]
    public string? Detail { get; set; }
}
