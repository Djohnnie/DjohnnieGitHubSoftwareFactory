using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Djohnnie.SoftwareFactory.Contracts.AiModels;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

/// <summary>
/// Scrapes the model pricing tables from the GitHub Copilot "Models and pricing" documentation page.
/// </summary>
public partial class ModelPricingScraper(HttpClient httpClient)
{
    public const string DefaultUrl = "https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing#pricing-tables";

    public async Task<IReadOnlyList<AiModel>> ScrapeAsync(string url, CancellationToken cancellationToken = default)
    {
        var uri = ValidateUrl(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder(uri) { Fragment = string.Empty }.Uri);
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Fetching {uri} failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var models = Parse(html, uri.ToString());
        if (models.Count == 0)
        {
            throw new InvalidOperationException("No model pricing tables were found on that page.");
        }
        return models;
    }

    public static Uri ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "docs.github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Enter an https://docs.github.com/... URL.", nameof(url));
        }
        return uri;
    }

    public static IReadOnlyList<AiModel> Parse(string html, string? sourceUrl = null)
    {
        var document = new HtmlParser().ParseDocument(html);
        var models = new List<AiModel>();

        foreach (var table in document.QuerySelectorAll("table"))
        {
            var headers = table.QuerySelectorAll("thead th").Select(th => Clean(th.TextContent)).ToList();
            int Column(string name)
            {
                return headers.FindIndex(h => h.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            }

            var model = Column("Model");
            var input = headers.FindIndex(h => h.Equals("Input", StringComparison.OrdinalIgnoreCase));
            var output = headers.FindIndex(h => h.Equals("Output", StringComparison.OrdinalIgnoreCase));
            if (model < 0 || input < 0 || output < 0)
            {
                continue;
            }

            var provider = ProviderOf(document, table);
            var status = Column("Release status");
            var category = Column("Category");
            var tier = Column("Tier");
            var threshold = Column("Threshold");
            var cachedInput = Column("Cached input");
            var cacheWrite = Column("Cache write");

            foreach (var row in table.QuerySelectorAll("tbody tr"))
            {
                var cells = row.QuerySelectorAll("td").ToList();
                string? Cell(int index)
                {
                    return index >= 0 && index < cells.Count ? CellText(cells[index]) : null;
                }

                var name = Cell(model);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                models.Add(new AiModel
                {
                    Name = name,
                    ModelId = ToModelId(name),
                    Provider = provider,
                    ReleaseStatus = NullIfNotApplicable(Cell(status)),
                    Category = NullIfNotApplicable(Cell(category)),
                    Tier = NullIfNotApplicable(Cell(tier)) ?? AiModel.DefaultTier,
                    Threshold = NullIfNotApplicable(Cell(threshold)),
                    InputPrice = ParsePrice(Cell(input)),
                    CachedInputPrice = ParsePrice(Cell(cachedInput)),
                    CacheWritePrice = ParsePrice(Cell(cacheWrite)),
                    OutputPrice = ParsePrice(Cell(output)),
                    SourceUrl = sourceUrl,
                    IsEnabled = true
                });
            }
        }

        // The same model/tier can only exist once.
        return models
            .GroupBy(m => (m.Name.ToLowerInvariant(), m.Tier.ToLowerInvariant()))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Derives the agent <c>model</c> value from a display name, e.g. "Claude Opus 4.8 (fast mode) (preview)" → "claude-opus-4.8-fast".</summary>
    public static string ToModelId(string name)
    {
        var id = PreviewSuffix().Replace(name, string.Empty);
        id = ModeSuffix().Replace(id, " $1");
        id = InvalidModelIdChars().Replace(id.ToLowerInvariant(), "-");
        return MultipleDashes().Replace(id, "-").Trim('-', '.');
    }

    public static decimal? ParsePrice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var match = PriceValue().Match(value);
        return match.Success && decimal.TryParse(match.Groups[1].Value.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? price
            : null;
    }

    private static string? ProviderOf(IDocument document, IElement table)
    {
        var labelledBy = table.GetAttribute("aria-labelledby");
        var heading = labelledBy is null ? null : document.GetElementById(labelledBy);
        if (heading is null)
        {
            for (var element = table.PreviousElementSibling; element is not null; element = element.PreviousElementSibling)
            {
                if (element.LocalName is "h2" or "h3" or "h4")
                {
                    heading = element;
                    break;
                }
            }
        }
        return heading is null ? null : Clean(heading.TextContent);
    }

    private static string CellText(IElement cell)
    {
        // Footnote references (<sup><a>1</a></sup>) are not part of the value.
        foreach (var sup in cell.QuerySelectorAll("sup").ToList())
        {
            sup.Remove();
        }
        return Clean(FootnoteMarker().Replace(cell.TextContent, string.Empty));
    }

    private static string? NullIfNotApplicable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Equals("Not applicable", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static string Clean(string value)
    {
        return Whitespace().Replace(value, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\[\^[^\]]*\]")]
    private static partial Regex FootnoteMarker();

    [GeneratedRegex(@"\$\s*([0-9][0-9,]*(?:\.[0-9]+)?)")]
    private static partial Regex PriceValue();

    [GeneratedRegex(@"\(\s*preview\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewSuffix();

    [GeneratedRegex(@"\(\s*([^)]*?)\s+mode\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ModeSuffix();

    [GeneratedRegex(@"[^a-z0-9.]+")]
    private static partial Regex InvalidModelIdChars();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultipleDashes();
}
