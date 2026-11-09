using Bunit;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class PageBreadcrumbsTests : BunitContext
{
    private const string BackSelector = "a[aria-label='Back']";

    public PageBreadcrumbsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void Overview_HasNothingToGoBackTo()
    {
        var cut = Render<PageBreadcrumbs>();

        Assert.Contains("Overview", cut.Markup);
        Assert.Empty(cut.FindAll(BackSelector));
    }

    [Fact]
    public void TopLevelPage_GoesBackToTheOverview()
    {
        var cut = Render<PageBreadcrumbs>(p => p.Add(c => c.Current, "Models"));

        Assert.Equal("/", cut.Find(BackSelector).GetAttribute("href"));
        Assert.Contains("Models", cut.Markup);
    }

    [Fact]
    public void DetailPage_GoesBackToItsLastParent_UnlessOverridden()
    {
        IReadOnlyList<BreadcrumbItem> parents = [new("Stories", "/stories"), new("Parent feature", "/features/1")];

        var cut = Render<PageBreadcrumbs>(p => p.Add(c => c.Parents, parents).Add(c => c.Current, "Story"));
        Assert.Equal("/features/1", cut.Find(BackSelector).GetAttribute("href"));

        cut.Render(p => p.Add(c => c.BackHref, "/stories"));
        Assert.Equal("/stories", cut.Find(BackSelector).GetAttribute("href"));
    }
}
