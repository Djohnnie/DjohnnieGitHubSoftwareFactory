using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>A signed-in session, so sign-ins survive restarts and deployments of the app.</summary>
public sealed class AuthSession
{
    /// <summary>The random key the session cookie holds.</summary>
    [Key, MaxLength(Contracts.AuthSessions.AuthSession.MaxKeyLength)]
    public string Key { get; set; } = string.Empty;

    /// <summary>The authentication ticket, serialized and encrypted with data protection (it contains the GitHub token).</summary>
    [Required]
    public byte[] Ticket { get; set; } = [];

    public DateTimeOffset ExpiresAt { get; set; }
}
