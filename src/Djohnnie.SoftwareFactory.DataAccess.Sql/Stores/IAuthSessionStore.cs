using Djohnnie.SoftwareFactory.Contracts.AuthSessions;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IAuthSessionStore
{
    /// <summary>The session, or <c>null</c> when it doesn't exist or has expired.</summary>
    Task<AuthSession?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a session, and removes the sessions that have expired.</summary>
    Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
