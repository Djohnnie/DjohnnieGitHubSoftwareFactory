using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Api = Djohnnie.SoftwareFactory.Contracts.GitHub.Api;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GitHubApiException(HttpStatusCode statusCode, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

/// <summary>The GitHub REST and GraphQL client: sending requests with the signed-in token, errors, paging and repositories. The other areas are in the GitHubApiClient.*.cs files.</summary>
public sealed partial class GitHubApiClient(HttpClient httpClient, IGitHubTokenProvider tokenProvider)
{
    private const int PageSize = 100;

    /// <summary>The account the token belongs to: the author of everything posted with it.</summary>
    public async Task<GitHubOwner> GetAuthenticatedUserAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "user", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubOwner>(cancellationToken)
            ?? throw new GitHubApiException(HttpStatusCode.InternalServerError, "GitHub returned an empty user.");
    }

    public async Task<GitHubRepository> GetRepositoryAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubRepository>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>Lists all repositories the signed-in user can access, most recently pushed first.</summary>
    public async Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        var repositories = new List<GitHubRepository>();
        string? url = $"user/repos?sort=pushed&direction=desc&per_page={PageSize}&affiliation=owner,collaborator,organization_member";

        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<List<GitHubRepository>>(cancellationToken) ?? [];
            repositories.AddRange(page);
            url = GetNextLink(response);
        }

        return repositories
            .OrderByDescending(r => r.PushedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static async Task<bool> IsRateLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0")
        {
            return true;
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Owner, string Name) SplitFullName(string repositoryFullName)
    {
        var parts = repositoryFullName.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"'{repositoryFullName}' is not an owner/name repository name.", nameof(repositoryFullName));
        }
        return (parts[0], parts[1]);
    }

    private async Task<List<T>> GetAllPagesAsync<T>(string firstUrl, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        string? url = firstUrl;
        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            items.AddRange(await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? []);
            url = GetNextLink(response);
        }
        return items;
    }

    /// <param name="accept">A media type instead of the client's default <c>application/vnd.github+json</c>.</param>
    /// <param name="apiVersion">A REST API version instead of the client's default one.</param>
    /// <param name="readOnly">The request changes nothing although its method isn't GET (a GraphQL query), so it may be retried.</param>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken,
        string? accept = null, string? apiVersion = null, bool readOnly = false)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetAccessTokenAsync(cancellationToken));
        // A header on the request replaces the client's default header of the same name.
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }
        if (apiVersion is not null)
        {
            request.Headers.Add(ApiVersionHeader, apiVersion);
        }
        if (readOnly)
        {
            request.Options.Set(GitHubApiResilience.ReadOnly, true);
        }
        var response = await SendRequestAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        ReportWhenUnauthorized(response);
        return response;
    }

    /// <summary>
    /// Sends through the resilience pipeline and turns its refusals into <see cref="GitHubApiException"/>, one of the
    /// exceptions the UI expects; a timeout or an open circuit is GitHub being unavailable, not a bug.
    /// </summary>
    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, completionOption, cancellationToken);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new GitHubApiException(HttpStatusCode.GatewayTimeout, "GitHub didn't answer in time. Try again later.", ex);
        }
        catch (BrokenCircuitException ex)
        {
            throw new GitHubApiException(HttpStatusCode.ServiceUnavailable, "GitHub is failing repeatedly, so requests are paused for a moment. Try again shortly.", ex);
        }
        catch (ExecutionRejectedException ex)
        {
            throw new GitHubApiException(HttpStatusCode.ServiceUnavailable, "Too many GitHub requests at once. Try again shortly.", ex);
        }
    }

    /// <summary>A 401 means the token itself is no longer valid (expired or revoked), so the session that owns it is ended.</summary>
    private void ReportWhenUnauthorized(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenProvider.ReportUnauthorized();
        }
    }

    public const string ApiVersionHeader = "X-GitHub-Api-Version";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            detail = ErrorDetail(await response.Content.ReadFromJsonAsync<Api.ErrorResponse>(cancellationToken));
        }
        catch
        {
            // Not a JSON error body; fall back to the status code.
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your GitHub session has expired. Please sign out and sign in again.",
            HttpStatusCode.Conflict => "The file was changed on GitHub in the meantime. Reload and try again.",
            _ => $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}{(detail is null ? string.Empty : $": {detail}")}"
        };

        throw new GitHubApiException(response.StatusCode, message);
    }

    /// <summary>GitHub's message followed by the reasons in its <c>errors</c>, which say what was wrong when the message is only "Unprocessable Entity".</summary>
    public static string? ErrorDetail(Api.ErrorResponse? error)
    {
        if (error is null)
        {
            return null;
        }

        var reasons = new List<string>();
        if (error.Errors is { ValueKind: JsonValueKind.Array } errors)
        {
            foreach (var entry in errors.EnumerateArray())
            {
                var reason = entry.ValueKind switch
                {
                    JsonValueKind.String => entry.GetString(),
                    JsonValueKind.Object when entry.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String => text.GetString(),
                    JsonValueKind.Object => string.Join(" ", new[] { "field", "code" }
                        .Select(name => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    reasons.Add(reason.Trim());
                }
            }
        }

        var message = string.IsNullOrWhiteSpace(error.Message) ? null : error.Message.Trim();
        if (reasons.Count == 0)
        {
            return message;
        }
        var joined = string.Join("; ", reasons);
        return message is null || message.Equals("Unprocessable Entity", StringComparison.OrdinalIgnoreCase) ? joined : $"{message} ({joined})";
    }

    private static string? GetNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        // Format: <https://api.github.com/...&page=2>; rel="next", <...>; rel="last". The URLs themselves can contain
        // commas (e.g. affiliation=owner,collaborator), so match whole <url>; rel="..." entries instead of splitting on ','.
        foreach (Match link in LinkEntry().Matches(string.Join(", ", values)))
        {
            if (link.Groups["rel"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("next"))
            {
                return link.Groups["url"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex("""<(?<url>[^>]*)>\s*;\s*rel="(?<rel>[^"]*)"\s*""")]
    private static partial Regex LinkEntry();
}
