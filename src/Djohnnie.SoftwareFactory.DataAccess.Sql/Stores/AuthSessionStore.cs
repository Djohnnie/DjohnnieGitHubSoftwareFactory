using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class AuthSessionStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IAuthSessionStore
{
    public async Task<AuthSession?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var entity = await db.AuthSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Key == key && s.ExpiresAt > now, cancellationToken);
        return entity is null ? null : new AuthSession(entity.Key, entity.Ticket, entity.ExpiresAt);
    }

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        // A single user has a handful of sessions, so loading the expired ones to remove them is cheap.
        db.AuthSessions.RemoveRange(await db.AuthSessions.Where(s => s.ExpiresAt <= now && s.Key != session.Key).ToListAsync(cancellationToken));

        var entity = await db.AuthSessions.SingleOrDefaultAsync(s => s.Key == session.Key, cancellationToken);
        if (entity is null)
        {
            entity = new Entities.AuthSession { Key = session.Key };
            db.AuthSessions.Add(entity);
        }
        entity.Ticket = session.Ticket;
        entity.ExpiresAt = session.ExpiresAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.AuthSessions.SingleOrDefaultAsync(s => s.Key == key, cancellationToken) is { } entity)
        {
            db.AuthSessions.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
