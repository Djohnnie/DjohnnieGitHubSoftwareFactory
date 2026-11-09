using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Sync;

internal sealed class ConfiguredGitHubTokenProvider(IOptions<GitHubSyncOptions> options) : IGitHubTokenProvider
{
    public Task<string> GetAccessTokenAsync()
    {
        return Task.FromResult(options.Value.SyncToken);
    }
}
