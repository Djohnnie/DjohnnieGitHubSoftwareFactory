namespace Djohnnie.SoftwareFactory.Contracts.AuthSessions;

/// <summary>A signed-in session: the protected authentication ticket (with the GitHub token) behind the session cookie.</summary>
/// <param name="Key">The random key the session cookie holds.</param>
/// <param name="Ticket">The serialized ticket, encrypted with ASP.NET Core data protection.</param>
/// <param name="ExpiresAt">When the session expires; renewed while it is used (sliding expiration).</param>
public sealed record AuthSession(string Key, byte[] Ticket, DateTimeOffset ExpiresAt)
{
    public const int MaxKeyLength = 64;
}
