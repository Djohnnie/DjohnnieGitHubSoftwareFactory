using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class SyncLogStore(IDbContextFactory<AppDbContext> dbFactory) : ISyncLogStore
{
    public async Task AddAsync(IReadOnlyList<SyncLogEvent> events, CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.SyncLogEvents.AddRange(events.Select(ToEntity));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddOrRepeatAsync(SyncLogEvent entry, Func<int, string> messageFor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var newest = await db.SyncLogEvents
            .OrderByDescending(e => e.LastOccurredAt)
            .ThenByDescending(e => e.SysId)
            .FirstOrDefaultAsync(cancellationToken);
        if (newest is not null && newest.Kind == entry.Kind)
        {
            newest.Iterations++;
            newest.LastOccurredAt = entry.LastOccurredAt;
            newest.RunId = entry.RunId;
            newest.Level = entry.Level;
            newest.Message = Truncate(messageFor(newest.Iterations), Contracts.SyncLog.SyncLogEvent.MaxMessageLength);
        }
        else
        {
            db.SyncLogEvents.Add(ToEntity(entry with { Iterations = 1, Message = messageFor(1) }));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<SyncLogEvent>> GetPageAsync(SyncLogQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var events = db.SyncLogEvents.AsNoTracking().Where(e => e.Level >= query.MinimumLevel);
        if (query.RepositoryFullName is { } repository)
        {
            events = events.Where(e => e.RepositoryFullName == repository);
        }

        var total = await events.CountAsync(cancellationToken);
        // A batch insert doesn't promise identity values in list order, so the time decides; SysId only breaks ties.
        var page = await events
            .OrderByDescending(e => e.LastOccurredAt)
            .ThenByDescending(e => e.SysId)
            .Skip(query.Page * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new(page.Select(e => e.ToContract()).ToList(), total);
    }

    public async Task<List<string>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SyncLogEvents.AsNoTracking()
            .Where(e => e.RepositoryFullName != null)
            .Select(e => e.RepositoryFullName!)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
    }

    private static Entities.SyncLogEvent ToEntity(SyncLogEvent entry)
    {
        return new()
        {
            Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
            RunId = entry.RunId,
            OccurredAt = entry.OccurredAt,
            LastOccurredAt = entry.LastOccurredAt,
            Iterations = Math.Max(1, entry.Iterations),
            Level = entry.Level,
            Kind = entry.Kind,
            RepositoryFullName = entry.RepositoryFullName,
            IssueNumber = entry.IssueNumber,
            WorkItemId = entry.WorkItemId,
            // A long GitHub error message shouldn't cost the whole run its events.
            Message = Truncate(entry.Message, Contracts.SyncLog.SyncLogEvent.MaxMessageLength),
            Detail = entry.Detail is null ? null : Truncate(entry.Detail, Contracts.SyncLog.SyncLogEvent.MaxDetailLength)
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length > maxLength ? string.Concat(text.AsSpan(0, maxLength - 1), "…") : text;
    }
}
