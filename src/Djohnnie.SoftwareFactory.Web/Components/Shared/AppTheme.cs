using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2da44e",
            Secondary = "#57606a",
            Tertiary = "#0969da",
            AppbarBackground = "#24292f",
            AppbarText = "#ffffff",
            Background = "#f6f8fa"
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