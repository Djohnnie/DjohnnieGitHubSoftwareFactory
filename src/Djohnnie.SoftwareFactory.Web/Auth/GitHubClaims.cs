namespace Djohnnie.SoftwareFactory.Web.Auth;

public static class GitHubClaims
{
    /// <summary>
    /// The GitHub OAuth access token. It lives in the server-side ticket store only, so interactive
    /// circuits (which have no reliable HttpContext) can still call the GitHub API.
    /// </summary>
    public const string AccessToken = "urn:dghsf:github:access_token";
}
