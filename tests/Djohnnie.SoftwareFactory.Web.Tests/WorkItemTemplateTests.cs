using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
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

public class WorkItemTemplateTests : BunitContext
{
    public WorkItemTemplateTests()
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
        var service = Services.GetRequiredService<WorkItemTemplateService>();
        var template = await service.SaveAsync(new WorkItemTemplate
        {
            Type = WorkItemType.Epic,
            Name = " Feature ",
            TitleTemplate = " Add [capability] ",
            Description = "## Goal\n\n<!-- Describe the goal -->\n\n## Acceptance criteria\n- [ ] Done\n"
        }, TestContext.Current.CancellationToken);
        Assert.NotEqual(Guid.Empty, template.Id);
        Assert.Equal("Feature", template.Name);
        Assert.Equal("Add [capability]", template.TitleTemplate);
        var loaded = await service.GetAsync(template.Id, TestContext.Current.CancellationToken);
        Assert.Equal(template, loaded);
        loaded!.Description = "Unsaved change";
        Assert.Equal(template.Description, (await service.GetAsync(template.Id, TestContext.Current.CancellationToken))!.Description);

        var updated = await service.SaveAsync(template with { Name = "Updated", Description = "## Updated\n", Type = WorkItemType.Bug }, TestContext.Current.CancellationToken);
        Assert.Equal(template.CreatedAt, updated.CreatedAt);
        Assert.Equal(WorkItemType.Epic, updated.Type);
        Assert.Equal("## Updated\n", Assert.Single(await service.GetAllAsync(WorkItemType.Epic, TestContext.Current.CancellationToken)).Description);

        await service.DeleteAsync(template.Id, TestContext.Current.CancellationToken);
        Assert.Empty(await service.GetAllAsync(WorkItemType.Epic, TestContext.Current.CancellationToken));
        Assert.Null(await service.GetAsync(template.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => service.SaveAsync(updated, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAll_OnlyReturnsTemplatesOfTheType()
    {
        var service = Services.GetRequiredService<WorkItemTemplateService>();
        foreach (var type in WorkItemTypeRules.All)
        {
            await service.SaveAsync(new WorkItemTemplate { Type = type, Name = type.DisplayName, TitleTemplate = "Example", Description = "## Body" },
                TestContext.Current.CancellationToken);
        }

        foreach (var type in WorkItemTypeRules.All)
        {
            var template = Assert.Single(await service.GetAllAsync(type, TestContext.Current.CancellationToken));
            Assert.Equal((type, type.DisplayName), (template.Type, template.Name));
        }
    }

    [Fact]
    public async Task SaveAsDefault_ReplacesTheDefaultOfTheSameTypeOnly()
    {
        var service = Services.GetRequiredService<WorkItemTemplateService>();
        var ct = TestContext.Current.CancellationToken;
        var bug = await service.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Bug, Name = "Bug", TitleTemplate = "Example", Description = "## Body", IsDefault = true }, ct);
        var first = await service.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Epic, Name = "First", TitleTemplate = "Example", Description = "## Body", IsDefault = true }, ct);
        var second = await service.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Epic, Name = "Second", TitleTemplate = "Example", Description = "## Body" }, ct);
        Assert.Equal(first.Id, WorkItemTemplate.Proposed(await service.GetAllAsync(WorkItemType.Epic, ct))!.Id);

        await service.SaveAsync(second with { IsDefault = true }, ct);

        Assert.Equal([second.Id], (await service.GetAllAsync(WorkItemType.Epic, ct)).Where(t => t.IsDefault).Select(t => t.Id));
        Assert.True((await service.GetAsync(bug.Id, ct))!.IsDefault);
        Assert.Equal(first.UpdatedAt, (await service.GetAsync(first.Id, ct))!.UpdatedAt);

        await service.SaveAsync((await service.GetAsync(second.Id, ct))! with { IsDefault = false }, ct);
        var templates = await service.GetAllAsync(WorkItemType.Epic, ct);
        Assert.DoesNotContain(templates, t => t.IsDefault);
        Assert.Equal(first.Id, WorkItemTemplate.Proposed(templates)!.Id);
        Assert.Null(WorkItemTemplate.Proposed([]));
    }

    [Fact]
    public async Task EditorSwitch_StoresTheDefault_AndTheListMarksIt()
    {
        var service = Services.GetRequiredService<WorkItemTemplateService>();
        var ct = TestContext.Current.CancellationToken;
        var template = await service.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Task, Name = "Chore", TitleTemplate = "Example", Description = "## Body" }, ct);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/task-templates/edit/{template.Id}");
        Render<MudPopoverProvider>();
        var editor = Render<WorkItemTemplateEditor>(p => p.Add(e => e.Id, template.Id));

        await editor.InvokeAsync(() => editor.FindComponents<MudSwitch<bool>>().Single(s => s.Instance.Label == "Default template").Instance.ValueChanged.InvokeAsync(true));
        await editor.InvokeAsync(() => editor.FindAll("button").Single(b => b.TextContent.Contains("Save template")).Click());

        Assert.True((await service.GetAsync(template.Id, ct))!.IsDefault);
        var list = Render<WorkItemTemplates>();
        Assert.Equal("Default", list.FindComponent<MudChip<string>>().Find(".mud-chip-content").TextContent.Trim());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("type")]
    [InlineData("long-name")]
    [InlineData("long-title")]
    [InlineData("long-description")]
    public async Task Save_RejectsInvalidTemplateWithoutPersisting(string invalidField)
    {
        var template = new WorkItemTemplate { Type = WorkItemType.Story, Name = "Feature", TitleTemplate = "Add [capability]", Description = "## Goal" };
        switch (invalidField)
        {
            case "name": template.Name = " "; break;
            case "title": template.TitleTemplate = " "; break;
            case "description": template.Description = " "; break;
            case "type": template.Type = (WorkItemType)42; break;
            case "long-name": template.Name = new string('a', 201); break;
            case "long-title": template.TitleTemplate = new string('a', 257); break;
            case "long-description": template.Description = new string('a', WorkItem.MaxDescriptionLength + 1); break;
        }
        var service = Services.GetRequiredService<WorkItemTemplateService>();

        await Assert.ThrowsAsync<ValidationFailedException>(() => service.SaveAsync(template, TestContext.Current.CancellationToken));

        Assert.Empty(await service.GetAllAsync(WorkItemType.Story, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Editor_CreatesAndEditsTemplateOfTheRouteType_WithoutRepository()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/bug-templates/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemTemplateEditor>();
        Assert.Contains("New bug template", cut.Markup);
        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Template name").Find("input").Input("Crash");
        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title example").Find("input").Input("[Page] crashes when [action]");
        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("## Steps to reproduce\n\n## Expected"));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save template")).Click());

        var service = Services.GetRequiredService<WorkItemTemplateService>();
        var template = Assert.Single(await service.GetAllAsync(WorkItemType.Bug, TestContext.Current.CancellationToken));
        Assert.Equal("Crash", template.Name);
        Assert.Equal("[Page] crashes when [action]", template.TitleTemplate);
        Assert.Equal("## Steps to reproduce\n\n## Expected", template.Description);
        Assert.EndsWith("/bug-templates", navigation.Uri);

        navigation.NavigateTo($"/bug-templates/edit/{template.Id}");
        var edit = Render<WorkItemTemplateEditor>(p => p.Add(e => e.Id, template.Id));
        Assert.Equal(template.Description, edit.FindComponent<MarkdownEditor>().Instance.Value);
        edit.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Template name").Find("input").Input("Improved");
        await edit.InvokeAsync(() => edit.FindAll("button").Single(b => b.TextContent.Contains("Save template")).Click());
        Assert.Equal("Improved", (await service.GetAsync(template.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task List_ShowsTheRouteType_LinksToCreateItem_AndConfirmsDeletion()
    {
        var service = Services.GetRequiredService<WorkItemTemplateService>();
        var template = await service.SaveAsync(new WorkItemTemplate
        {
            Type = WorkItemType.Story, Name = "Story", TitleTemplate = "As a [user] I want [goal]", Description = "## Acceptance criteria"
        }, TestContext.Current.CancellationToken);
        await service.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Epic, Name = "Epic only", TitleTemplate = "Epic example", Description = "## Goal" },
            TestContext.Current.CancellationToken);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/story-templates");
        var dialogs = Render<MudDialogProvider>();
        var cut = Render<WorkItemTemplates>();
        Assert.Contains("User story templates", cut.Markup);
        Assert.Equal($"/stories/new?template={template.Id}", cut.Find("a[aria-label='Create user story']").GetAttribute("href"));
        Assert.Contains("As a [user] I want [goal]", cut.Markup);
        Assert.DoesNotContain("Epic example", cut.Markup);

        await cut.InvokeAsync(() => cut.Find("button[title='Delete']").Click());
        Assert.Single(await service.GetAllAsync(WorkItemType.Story, TestContext.Current.CancellationToken));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Delete").Click());

        cut.WaitForAssertion(() => Assert.DoesNotContain("As a [user] I want [goal]", cut.Markup));
        Assert.Empty(await service.GetAllAsync(WorkItemType.Story, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Editor_TemplateOfAnotherTypeOrMissing_ShowsErrorAndNoForm()
    {
        var epicTemplate = await Services.GetRequiredService<WorkItemTemplateService>().SaveAsync(new WorkItemTemplate
        {
            Type = WorkItemType.Epic, Name = "Epic", TitleTemplate = "Example", Description = "## Goal"
        }, TestContext.Current.CancellationToken);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/task-templates/edit/{epicTemplate.Id}");

        var cut = Render<WorkItemTemplateEditor>(p => p.Add(e => e.Id, epicTemplate.Id));

        Assert.Contains("This task template does not exist.", cut.Markup);
        Assert.Empty(cut.FindComponents<MarkdownEditor>());
    }
}
