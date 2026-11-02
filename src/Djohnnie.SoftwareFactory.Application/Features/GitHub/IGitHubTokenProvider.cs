namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public interface IGitHubTokenProvider
{
    Task<string> GetAccessTokenAsync();
}
