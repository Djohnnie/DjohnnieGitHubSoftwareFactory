using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#af43a0",
            Secondary = "#6b6078",
            Tertiary = "#4c5fd5",
            Info = "#2f78c4",
            Success = "#1c8257",
            Warning = "#b85f0b",
            Error = "#d1344b",
            AppbarBackground = "#2a2136",
            AppbarText = "#ffffff",
            Background = "#faf7fb",
            TableStriped = "#fbf9fc"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px"
        },
        Shadows = new Shadow
        {
            Elevation = [.. new Shadow().Elevation.Select(_ => "none")]
        }
    };
}