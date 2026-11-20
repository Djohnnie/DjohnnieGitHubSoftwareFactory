namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// A GitHub token from the configuration, for the function apps: nobody is signed in when a timer or queue message
/// triggers them, so they can't use a user's OAuth token.
/// </summary>
public sealed class ConfiguredGitHubTokenProvider(string token) : IGitHubTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(token);
    }
}
