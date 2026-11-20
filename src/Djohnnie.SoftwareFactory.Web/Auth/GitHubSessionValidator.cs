using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Djohnnie.SoftwareFactory.Web.Auth;

/// <summary>
/// Checks whether the GitHub access token of a session is still accepted by GitHub, so an expired or revoked token ends
/// the session (and the user signs in again) instead of every GitHub call failing with a 401.
/// </summary>
public sealed class GitHubSessionValidator(IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger<GitHubSessionValidator> logger)
{
    public const string HttpClientName = "GitHubSessionValidator";

    /// <summary>How often a session's token is checked against GitHub, on requests and in open circuits.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private const string ValidatedAtItem = "dghsf:github:validated_at";

    /// <summary>
    /// <c>false</c> only when GitHub rejects the token (401); <c>true</c> otherwise. When GitHub can't be reached the session
    /// is kept: an outage is no reason to sign the user out.
    /// </summary>
    public async Task<bool> IsValidAsync(string? accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            return response.StatusCode != HttpStatusCode.Unauthorized;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not validate the GitHub session; it is kept for now.");
            return true;
        }
    }

    /// <summary>
    /// The cookie handler's principal validation: rejects the session when its token has expired (as far as GitHub told us
    /// at sign-in) or GitHub no longer accepts it. GitHub is asked at most once per <see cref="Interval"/>.
    /// </summary>
    public async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var accessToken = context.Principal?.FindFirst(GitHubClaims.AccessToken)?.Value;
        var now = timeProvider.GetUtcNow();

        if (IsExpired(context.Properties, now) || (IsCheckDue(context.Properties, now) && !await IsValidAsync(accessToken, context.HttpContext.RequestAborted)))
        {
            logger.LogInformation("The GitHub session of {Login} has expired; signing out.", context.Principal?.FindFirst(ClaimTypes.Name)?.Value);
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        if (IsCheckDue(context.Properties, now))
        {
            context.Properties.Items[ValidatedAtItem] = now.ToString("O", CultureInfo.InvariantCulture);
            context.ShouldRenew = true;
        }
    }

    private static bool IsExpired(AuthenticationProperties properties, DateTimeOffset now)
    {
        // Only present for tokens that expire (e.g. GitHub App user tokens); OAuth App tokens live until revoked.
        return DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
            && expiresAt <= now;
    }

    private static bool IsCheckDue(AuthenticationProperties properties, DateTimeOffset now)
    {
        return !properties.Items.TryGetValue(ValidatedAtItem, out var value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var validatedAt)
            || now - validatedAt >= Interval;
    }
}
