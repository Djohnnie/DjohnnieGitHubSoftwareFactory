namespace Djohnnie.SoftwareFactory.Application.Features.Speech;

/// <summary>
/// The Azure AI Speech resource used to dictate titles. The browser recognizes speech directly with Azure using a
/// short-lived token issued with <see cref="ApiKey"/>; the key stays on the server. Unconfigured means dictation is
/// disabled and the dictation buttons are hidden.
/// </summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>The Azure region of the Speech resource, e.g. <c>westeurope</c>.</summary>
    public string? Region { get; set; }

    /// <summary>Key 1 or key 2 of the Speech resource.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional endpoint of the resource (e.g. <c>https://&lt;custom-domain&gt;.cognitiveservices.azure.com/</c>) to issue
    /// tokens from; defaults to the regional endpoint.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>One locale such as <c>en-US</c>, or up to four comma-separated locales (e.g. <c>en-US,nl-BE</c>) to detect the spoken one.</summary>
    public string Language { get; set; } = "en-US";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Region) && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>The configured locales; Azure detects the spoken language among at most four at the start of a recognition.</summary>
    public IReadOnlyList<string> Languages
    {
        get
        {
            var languages = Language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
            return languages.Count > 0 ? languages : ["en-US"];
        }
    }

    public Uri TokenEndpoint
    {
        get
        {
            var baseAddress = string.IsNullOrWhiteSpace(Endpoint)
                ? $"https://{Region!.Trim()}.api.cognitive.microsoft.com/"
                : Endpoint.Trim().TrimEnd('/') + "/";
            return new Uri(new Uri(baseAddress), "sts/v1.0/issueToken");
        }
    }
}
