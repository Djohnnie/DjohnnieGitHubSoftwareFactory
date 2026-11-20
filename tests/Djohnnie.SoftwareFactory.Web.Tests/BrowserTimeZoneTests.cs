using Bunit;
using Djohnnie.SoftwareFactory.Web.Components.Shared;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>Timestamps in the UI are shown in the browser's time zone, not the server's.</summary>
public class BrowserTimeZoneTests : BunitContext
{
    private static readonly DateTimeOffset Winter = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Summer = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Use_AnIanaZone_FollowsItsDaylightSavingTime()
    {
        var zone = new BrowserTimeZone();

        zone.Use("Europe/Brussels", 60);

        Assert.Equal("2026-11-20 13:00:00", zone.Format(Winter));
        Assert.Equal("2026-07-01 14:00:00", zone.Format(Summer));
        Assert.Equal(TimeSpan.FromHours(1), zone.ToLocal(Winter).Offset);
    }

    [Fact]
    public void Use_AnUnknownZone_FallsBackToTheBrowsersOffset()
    {
        var zone = new BrowserTimeZone();

        zone.Use("Nowhere/Unknown", 330);

        Assert.Equal("2026-11-20 17:30", zone.Format(Winter, "yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public void Format_WithoutAMoment_IsNull()
    {
        Assert.Null(new BrowserTimeZone().Format((DateTimeOffset?)null));
    }

    [Fact]
    public async Task Initialize_AsksTheBrowserOnce()
    {
        var module = JSInterop.SetupModule(BrowserTimeZone.ModulePath);
        module.Setup<BrowserTimeZone.BrowserZone?>("timeZone").SetResult(new("America/New_York", -300));
        var zone = new BrowserTimeZone();

        await zone.InitializeAsync(JSInterop.JSRuntime);
        await zone.InitializeAsync(JSInterop.JSRuntime);

        Assert.True(zone.IsInitialized);
        Assert.Equal("2026-11-20 07:00:00", zone.Format(Winter));
        Assert.Single(module.Invocations);
    }

    [Fact]
    public async Task Initialize_WithoutTheBrowser_KeepsTheServersZone()
    {
        JSInterop.SetupModule(BrowserTimeZone.ModulePath).Setup<BrowserTimeZone.BrowserZone?>("timeZone").SetException(new Microsoft.JSInterop.JSException("no Intl"));
        var zone = new BrowserTimeZone();

        await zone.InitializeAsync(JSInterop.JSRuntime);

        Assert.True(zone.IsInitialized);
        Assert.Equal(TimeZoneInfo.Local, zone.Zone);
    }
}
