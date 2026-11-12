using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface ISyncLogStore
{
    /// <summary>Adds the events of one run in one save; their <see cref="SyncLogEvent.LastOccurredAt"/> orders them on the page.</summary>
    Task AddAsync(IReadOnlyList<SyncLogEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a run that had nothing to report. When the newest event is of the same kind, that event stands for one
    /// more run (its iterations go up; its last occurrence, run, level and message are updated); otherwise
    /// <paramref name="entry"/> is added. The message comes from <paramref name="messageFor"/>, given the number of runs.
    /// </summary>
    Task AddOrRepeatAsync(SyncLogEvent entry, Func<int, string> messageFor, CancellationToken cancellationToken = default);

    /// <summary>The events of the page, newest first, and how many events match the query.</summary>
    Task<PagedResult<SyncLogEvent>> GetPageAsync(SyncLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>The repositories that have events, alphabetically.</summary>
    Task<List<string>> GetRepositoriesAsync(CancellationToken cancellationToken = default);
}
