using System.Globalization;
using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// The resilience pipeline of <see cref="GitHubApiClient"/>: timeouts, a circuit breaker and retries of transient failures
/// and short rate-limit waits. Only requests that are safe to send twice are retried (GET and requests marked
/// <see cref="ReadOnly"/>), so a create, comment or merge is never repeated by a retry.
/// </summary>
public static class GitHubApiResilience
{
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(3);
    public const int MaxRetries = 3;

    /// <summary>
    /// The longest wait GitHub may ask for (<c>Retry-After</c> or <c>x-ratelimit-reset</c>) that is still retried. A longer
    /// one, such as the primary rate limit that resets hourly, fails at once instead of blocking the caller.
    /// </summary>
    public static readonly TimeSpan MaxRateLimitWait = TimeSpan.FromSeconds(60);

    /// <summary>Marks a request with a method that isn't safe (e.g. a GraphQL query, which is a POST) as safe to retry.</summary>
    public static readonly HttpRequestOptionsKey<bool> ReadOnly = new("dghsf:github:read-only");

    public static void Configure(HttpStandardResilienceOptions options, TimeProvider timeProvider)
    {
        options.AttemptTimeout.Timeout = AttemptTimeout;
        options.TotalRequestTimeout.Timeout = TotalTimeout;

        options.Retry.MaxRetryAttempts = MaxRetries;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.ShouldHandle = args =>
        {
            return ValueTask.FromResult(ShouldRetry(args.Outcome, args.Context.GetRequestMessage(), timeProvider.GetUtcNow()));
        };
        // GitHub's own wait when it gives one; otherwise (null) the exponential backoff with jitter.
        options.Retry.DelayGenerator = args =>
        {
            return ValueTask.FromResult(args.Outcome.Result is { } response ? RequestedWait(response, timeProvider.GetUtcNow()) : null);
        };

        // Must be at least twice the attempt timeout. A single user rarely sends 20 requests in a minute, so the breaker
        // only opens during a real outage, not after a couple of failures.
        options.CircuitBreaker.SamplingDuration = 2 * AttemptTimeout;
        options.CircuitBreaker.MinimumThroughput = 20;
    }

    public static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, HttpRequestMessage? request, DateTimeOffset now)
    {
        if (request is null || !IsSafeToRepeat(request))
        {
            return false;
        }
        if (outcome.Result is { } response && IsRateLimited(response))
        {
            return RequestedWait(response, now) is not { } wait || wait <= MaxRateLimitWait;
        }
        if (!HttpClientResiliencePredicates.IsTransient(outcome))
        {
            return false;
        }
        return outcome.Result is not { } failed || RequestedWait(failed, now) is not { } delay || delay <= MaxRateLimitWait;
    }

    private static bool IsSafeToRepeat(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options)
        {
            return true;
        }
        return request.Options.TryGetValue(ReadOnly, out var readOnly) && readOnly;
    }

    /// <summary>
    /// GitHub's primary and secondary rate limits: a 429, or a 403 that says so in its headers. A 403 without them is a
    /// permission problem, which a retry doesn't fix.
    /// </summary>
    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }
        return response.StatusCode == HttpStatusCode.Forbidden
            && (response.Headers.RetryAfter is not null || RemainingRequests(response) == 0);
    }

    /// <summary>How long GitHub asks to wait before the next request; <c>null</c> when it doesn't say.</summary>
    public static TimeSpan? RequestedWait(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
            }
            if (retryAfter.Date is { } date)
            {
                return date > now ? date - now : TimeSpan.Zero;
            }
        }

        if (RemainingRequests(response) == 0
            && response.Headers.TryGetValues("x-ratelimit-reset", out var values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetSeconds))
        {
            // The reset time has a one-second resolution, so wait a second past it.
            var reset = DateTimeOffset.FromUnixTimeSeconds(resetSeconds).AddSeconds(1);
            return reset > now ? reset - now : TimeSpan.Zero;
        }
        return null;
    }

    private static int? RemainingRequests(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining)
                ? remaining
                : null;
    }
}
