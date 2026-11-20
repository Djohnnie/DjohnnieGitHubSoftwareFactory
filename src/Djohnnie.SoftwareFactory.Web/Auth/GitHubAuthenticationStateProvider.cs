using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Djohnnie.SoftwareFactory.Web.Auth;

/// <summary>
/// The authentication state of an interactive circuit. A circuit keeps the user it started with for as long as the tab is
/// open, so the GitHub token is checked again periodically, and a 401 from GitHub signs the circuit out immediately. A
/// signed-out circuit is not authorized for any page anymore, which sends the user to the login page.
/// </summary>
public sealed class GitHubAuthenticationStateProvider(GitHubSessionValidator validator, ILoggerFactory loggerFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    private int _signedOut;

    protected override TimeSpan RevalidationInterval
    {
        get
        {
            return GitHubSessionValidator.Interval;
        }
    }

    /// <summary>Ends the circuit's session because GitHub rejected its token.</summary>
    public void SignOut()
    {
        if (Interlocked.Exchange(ref _signedOut, 1) == 0)
        {
            SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
        }
    }

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        return await validator.IsValidAsync(authenticationState.User.FindFirst(GitHubClaims.AccessToken)?.Value, cancellationToken);
    }
}
