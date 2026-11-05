using System.Security.Cryptography;
using Djohnnie.SoftwareFactory.Application.Features.AuthSessions;
using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Djohnnie.SoftwareFactory.Web.Auth;

/// <summary>
/// Keeps the authentication tickets (including the GitHub access token) server-side in SQL, so the cookie only holds a
/// random session key and sign-ins survive restarts, deployments and more than one instance. The tickets are encrypted
/// with ASP.NET Core data protection before they are stored.
/// </summary>
public sealed class SqlTicketStore(
    IServiceScopeFactory scopes, IDataProtectionProvider dataProtection, TimeProvider timeProvider, ILogger<SqlTicketStore> logger) : ITicketStore
{
    /// <summary>How long a session lasts without a renewal; matches the cookie's sliding expiration.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Djohnnie.SoftwareFactory.Web.Auth.SqlTicketStore");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        // 32 random bytes as 64 hex characters: unguessable, and exactly the maximum key length.
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(AuthSession.MaxKeyLength / 2));
        await SaveAsync(key, ticket);
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await SaveAsync(key, ticket);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<AuthSessionService>().GetAsync(key);
        if (session is null)
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Ticket));
        }
        catch (CryptographicException ex)
        {
            // E.g. the data protection keys changed: the user simply signs in again.
            logger.LogWarning(ex, "A stored session could not be decrypted; it is treated as signed out.");
            return null;
        }
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthSessionService>().RemoveAsync(key);
    }

    private async Task SaveAsync(string key, AuthenticationTicket ticket)
    {
        var expiresAt = ticket.Properties.ExpiresUtc ?? timeProvider.GetUtcNow() + Lifetime;
        var protectedTicket = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthSessionService>().SaveAsync(new AuthSession(key, protectedTicket, expiresAt));
    }
}
