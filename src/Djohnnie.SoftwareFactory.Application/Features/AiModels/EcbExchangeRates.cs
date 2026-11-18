using System.Globalization;
using System.Xml.Linq;
using Djohnnie.SoftwareFactory.Contracts;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

/// <summary>
/// Reads the daily euro foreign exchange reference rates published by the European Central Bank.
/// </summary>
public class EcbExchangeRates(HttpClient httpClient)
{
    public const string DailyRatesUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";

    /// <summary>Returns how many US dollars one euro is worth today, according to the ECB.</summary>
    public virtual async Task<decimal> GetUsdPerEurAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(DailyRatesUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DomainRuleException($"Fetching the ECB exchange rates failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseUsdPerEur(xml);
    }

    public static decimal ParseUsdPerEur(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            throw new DomainRuleException("The ECB exchange rates could not be read.");
        }

        // <Cube currency="USD" rate="1.0834"/> — matched by local name, the ECB document uses its own namespace.
        var rate = document.Descendants()
            .Where(e => e.Name.LocalName == "Cube" && (string?)e.Attribute("currency") == "USD")
            .Select(e => (string?)e.Attribute("rate"))
            .FirstOrDefault();

        if (!decimal.TryParse(rate, NumberStyles.Number, CultureInfo.InvariantCulture, out var usdPerEur) || usdPerEur <= 0)
        {
            throw new DomainRuleException("The ECB exchange rates do not contain a valid USD rate.");
        }
        return usdPerEur;
    }

    /// <summary>Converts a USD amount to EUR, rounded to the 4 decimals prices are stored with.</summary>
    public static decimal? ToEur(decimal? usd, decimal usdPerEur)
    {
        return usd is null ? null : Math.Round(usd.Value / usdPerEur, 4, MidpointRounding.AwayFromZero);
    }
}
