using System.Globalization;
using Microsoft.JSInterop;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// The time zone of the user's browser, for showing moments in the user's local time. The app renders on the server
/// (Interactive Server), where <see cref="DateTimeOffset.ToLocalTime"/> would give the server's time zone (UTC on Azure), so
/// every timestamp in the UI goes through <see cref="ToLocal(DateTimeOffset)"/> instead. One per circuit;
/// <see cref="Layout.MainLayout"/> reads the browser's zone before it renders any page. Until then, or when the browser's zone
/// can't be read, it is the server's zone.
/// </summary>
public sealed class BrowserTimeZone
{
    public const string ModulePath = "./js/timezone.js";

    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Local;

    /// <summary>Whether the browser was asked for its zone (also when that failed), so it isn't asked again.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>Asks the browser for its IANA time zone (and its current UTC offset as a fallback), once per circuit.</summary>
    public async Task InitializeAsync(IJSRuntime js)
    {
        if (IsInitialized)
        {
            return;
        }
        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (module is not null)
            {
                var browser = await module.InvokeAsync<BrowserZone?>("timeZone");
                if (browser is not null)
                {
                    Use(browser.Id, browser.OffsetMinutes);
                }
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            // No browser (static rendering, a disconnected circuit) or no zone: the server's zone it is.
        }
        IsInitialized = true;
    }

    /// <summary>
    /// Uses the IANA zone <paramref name="id"/> (e.g. <c>Europe/Brussels</c>), or, when this server doesn't know it, a fixed
    /// zone at <paramref name="offsetMinutes"/> ahead of UTC (no daylight saving time changes).
    /// </summary>
    public void Use(string? id, int offsetMinutes)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                Zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Fall back to the offset below.
            }
        }
        var offset = TimeSpan.FromMinutes(Math.Clamp(offsetMinutes, -14 * 60, 14 * 60));
        var name = $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
        Zone = TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name);
    }

    /// <summary>The moment in the user's time zone.</summary>
    public DateTimeOffset ToLocal(DateTimeOffset at)
    {
        return TimeZoneInfo.ConvertTime(at, Zone);
    }

    /// <inheritdoc cref="ToLocal(DateTimeOffset)"/>
    public DateTimeOffset? ToLocal(DateTimeOffset? at)
    {
        return at is { } value ? ToLocal(value) : null;
    }

    /// <summary>The moment in the user's time zone, written with <paramref name="format"/> (invariant culture).</summary>
    public string Format(DateTimeOffset at, string format = "yyyy-MM-dd HH:mm:ss")
    {
        return ToLocal(at).ToString(format, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc cref="Format(DateTimeOffset, string)"/>
    public string? Format(DateTimeOffset? at, string format = "yyyy-MM-dd HH:mm:ss")
    {
        return at is { } value ? Format(value, format) : null;
    }

    /// <summary>What <c>timeZone</c> in wwwroot/js/timezone.js returns.</summary>
    public sealed record BrowserZone(string? Id, int OffsetMinutes);
}
