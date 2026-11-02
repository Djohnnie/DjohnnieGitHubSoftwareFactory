using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Microsoft.AspNetCore.Components.Authorization;

namespace Djohnnie.SoftwareFactory.Web.Auth;

public sealed class GitHubTokenProvider(AuthenticationStateProvider authenticationStateProvider) : IGitHubTokenProvider
{
    public async Task<string> GetAccessTokenAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        return state.User.FindFirst(GitHubClaims.AccessToken)?.Value
            ?? throw new GitHubApiException(HttpStatusCode.Unauthorized, "No GitHub access token in the current session. Please sign out and sign in again.");
    }
}
