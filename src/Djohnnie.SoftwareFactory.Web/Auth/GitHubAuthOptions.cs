using System.Globalization;
using System.Security.Claims;

namespace Djohnnie.SoftwareFactory.Web.Auth;

public sealed class GitHubAuthOptions
{
    public const string SectionName = "GitHub";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The numeric id of the only GitHub account that is allowed to sign in (<c>https://api.github.com/users/{login}</c>
    /// shows it). Not the login: a login can be renamed and then claimed by someone else, the id never changes.
    /// </summary>
    public long AllowedUserId { get; set; }

    /// <summary>
    /// Whether the signed-in GitHub account is the allowed one: its id, which the GitHub handler stores as
    /// <see cref="ClaimTypes.NameIdentifier"/>, is <see cref="AllowedUserId"/>.
    /// </summary>
    public bool IsAllowed(ClaimsIdentity? identity)
    {
        return AllowedUserId > 0
            && long.TryParse(identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            && id == AllowedUserId;
    }
}
