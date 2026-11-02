using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using TestContext = Xunit.TestContext;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class EpicTemplateTests : BunitContext
{
    public EpicTemplateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLogging();
        Services.AddApplication();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(options));
    }

    [Fact]
    public async Task Crud_PreservesMarkdownAndCreationTime_AndReturnsDetachedCopies()
    {
        var service = Services.GetRequiredService<EpicTemplateService>();
        var template = await service.SaveAsync(new EpicTemplate
        {
            Name = " Feature ",
            TitleTemplate = " Add [capability] ",
            Description = "## Goal\n\n<!-- Describe the goal -->\n\n## Acceptance criteria\n- [ ] Done\n"
        }, TestContext.Current.CancellationToken);
        Assert.True(template.Id > 0);
        Assert.Equal("Feature", template.Name);
        Assert.Equal("Add [capability]", template.TitleTemplate);
        var loaded = await service.GetAsync(template.Id, TestContext.Current.CancellationToken);
        Assert.Equal(template, loaded);
        loaded!.Description = "Unsaved change";
        Assert.Equal(template.Description, (await service.GetAsync(template.Id, TestContext.Current.CancellationToken))!.Description);

        var updated = await service.SaveAsync(template with { Name = "Updated", Description = "## Updated\n" }, TestContext.Current.CancellationToken);
        Assert.Equal(template.CreatedAt, updated.CreatedAt);
        Assert.Equal("## Updated\n", Assert.Single(await service.GetAllAsync(TestContext.Current.CancellationToken)).Description);

        await service.DeleteAsync(template.Id, TestContext.Current.CancellationToken);
        Assert.Empty(await service.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Null(await service.GetAsync(template.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(updated, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("long-name")]
    [InlineData("long-title")]
    [InlineData("long-description")]
    public async Task Save_RejectsInvalidTemplateWithoutPersisting(string invalidField)
    {
        var template = new EpicTemplate { Name = "Feature", TitleTemplate = "Add [capability]", Description = "## Goal" };
        switch (invalidField)
        {
            case "name": template.Name = " "; break;
            case "title": template.TitleTemplate = " "; break;
            case "description": template.Description = " "; break;
            case "long-name": template.Name = new string('a', 201); break;
            case "long-title": template.TitleTemplate = new string('a', 257); break;
            case "long-description": template.Description = new string('a', Epic.MaxDescriptionLength + 1); break;
        }
        var service = Services.GetRequiredService<EpicTemplateService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(template, TestContext.Current.CancellationToken));

        Assert.Empty(await service.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Editor_CreatesAndEditsTemplate_WithoutRepository()
    {
        Render<MudPopoverProvider>();
        var cut = Render<EpicTemplateEditor>();
        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Template name").Find("input").Input("Feature");
        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title example").Find("input").Input("Add [capability]");
        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("## Goal\n\n## Acceptance criteria"));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save template")).Click());

        var service = Services.GetRequiredService<EpicTemplateService>();
        var template = Assert.Single(await service.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Feature", template.Name);
        Assert.Equal("Add [capability]", template.TitleTemplate);
        Assert.Equal("## Goal\n\n## Acceptance criteria", template.Description);
        Assert.EndsWith("/epic-templates", Services.GetRequiredService<NavigationManager>().Uri);

        var edit = Render<EpicTemplateEditor>(p => p.Add(e => e.Id, template.Id));
        Assert.Equal(template.Description, edit.FindComponent<MarkdownEditor>().Instance.Value);
        edit.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Template name").Find("input").Input("Improved");
        await edit.InvokeAsync(() => edit.FindAll("button").Single(b => b.TextContent.Contains("Save template")).Click());
        Assert.Equal("Improved", (await service.GetAsync(template.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task List_LinksToCreateEpic_AndConfirmsDeletion()
    {
        var service = Services.GetRequiredService<EpicTemplateService>();
        var template = await service.SaveAsync(new EpicTemplate
        {
            Name = "Feature", TitleTemplate = "Add [capability]", Description = "## Goal"
        }, TestContext.Current.CancellationToken);
        var dialogs = Render<MudDialogProvider>();
        var cut = Render<EpicTemplates>();
        Assert.Equal($"/epics/new?template={template.Id}", cut.Find("a[aria-label='Create epic']").GetAttribute("href"));
        Assert.Contains("Add [capability]", cut.Markup);

        await cut.InvokeAsync(() => cut.Find("button[title='Delete']").Click());
        Assert.Single(await service.GetAllAsync(TestContext.Current.CancellationToken));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Delete").Click());

        cut.WaitForAssertion(() => Assert.DoesNotContain("Add [capability]", cut.Markup));
        Assert.Empty(await service.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Editor_MissingTemplate_ShowsErrorAndNoForm()
    {
        var cut = Render<EpicTemplateEditor>(p => p.Add(e => e.Id, 999));

        Assert.Contains("This epic template does not exist.", cut.Markup);
        Assert.Empty(cut.FindComponents<MarkdownEditor>());
    }
}
