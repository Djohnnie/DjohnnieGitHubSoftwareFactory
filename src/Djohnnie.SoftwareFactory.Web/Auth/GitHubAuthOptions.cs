namespace Djohnnie.SoftwareFactory.Web.Auth;

public sealed class GitHubAuthOptions
{
    public const string SectionName = "GitHub";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The only GitHub login that is allowed to sign in.</summary>
    public string AllowedLogin { get; set; } = string.Empty;
}
