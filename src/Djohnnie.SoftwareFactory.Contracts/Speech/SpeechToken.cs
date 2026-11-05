namespace Djohnnie.SoftwareFactory.Contracts.Speech;

/// <summary>
/// A short-lived Azure AI Speech authorization token for speech-to-text in the browser, so the Speech key itself never
/// leaves the server.
/// </summary>
/// <param name="Token">The authorization token (valid for 10 minutes after it was issued).</param>
/// <param name="Region">The Azure region of the Speech resource, e.g. <c>westeurope</c>.</param>
/// <param name="Languages">One locale to recognize, or up to four to detect the spoken language from.</param>
/// <param name="ExpiresAt">When the token can no longer be used to start a recognition.</param>
public sealed record SpeechToken(string Token, string Region, IReadOnlyList<string> Languages, DateTimeOffset ExpiresAt);
