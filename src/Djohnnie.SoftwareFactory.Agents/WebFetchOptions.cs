namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>Settings for the <c>fetch_url</c> tool that lets the Copilots read web pages the user links to.</summary>
public sealed class WebFetchOptions
{
    public const string SectionName = "WebFetch";

    /// <summary><c>false</c> turns the <c>fetch_url</c> tool off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long one request, redirects included, may take.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>The most bytes read from a response; the rest is ignored.</summary>
    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>The most characters of page text the model gets; longer pages are cut off.</summary>
    public int MaxCharacters { get; set; } = 40_000;
}
