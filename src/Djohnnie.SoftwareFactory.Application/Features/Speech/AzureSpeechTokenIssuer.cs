using Djohnnie.SoftwareFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.Speech;

/// <summary>An Azure AI Speech authorization token and when it stops being usable.</summary>
public sealed record IssuedSpeechToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues short-lived Azure AI Speech authorization tokens, so the Speech key never reaches the browser.</summary>
public interface ISpeechTokenIssuer
{
    Task<IssuedSpeechToken> IssueAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Exchanges the Speech key for an authorization token at the resource's <c>sts/v1.0/issueToken</c> endpoint. Tokens are
/// valid for 10 minutes; one is reused until 2 minutes before that, so each dictation doesn't need a round trip.
/// </summary>
public sealed class AzureSpeechTokenIssuer(IHttpClientFactory httpClients, IOptions<SpeechOptions> options, TimeProvider time) : ISpeechTokenIssuer, IDisposable
{
    public const string HttpClientName = "AzureSpeech";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan ReuseMargin = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IssuedSpeechToken? _current;

    public async Task<IssuedSpeechToken> IssueAsync(CancellationToken cancellationToken)
    {
        var speech = options.Value;
        if (!speech.IsConfigured)
        {
            throw new DomainRuleException("Dictation is not configured: set Speech:Region and Speech:ApiKey.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_current is { } current && current.ExpiresAt - time.GetUtcNow() > ReuseMargin)
            {
                return current;
            }

            var issuedAt = time.GetUtcNow();
            using var request = new HttpRequestMessage(HttpMethod.Post, speech.TokenEndpoint) { Content = new ByteArrayContent([]) };
            request.Headers.Add("Ocp-Apim-Subscription-Key", speech.ApiKey);

            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Azure AI Speech refused the token request ({(int)response.StatusCode} {response.ReasonPhrase}). Check Speech:Region, Speech:Endpoint and Speech:ApiKey.",
                    null, response.StatusCode);
            }

            var token = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            _current = new IssuedSpeechToken(token, issuedAt + Lifetime);
            return _current;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
