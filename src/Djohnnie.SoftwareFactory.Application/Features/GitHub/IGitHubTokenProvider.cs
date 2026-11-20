namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public interface IGitHubTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when GitHub rejected the token (401): it expired or was revoked. A signed-in user's provider ends the session,
    /// so the user is sent to the login page instead of seeing errors; a configured token has nothing to end.
    /// </summary>
    void ReportUnauthorized()
    {
    }
}
