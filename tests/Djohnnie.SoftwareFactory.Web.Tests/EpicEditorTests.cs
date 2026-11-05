using System.Net;
using System.Security.Claims;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Epics;
using Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class EpicEditorTests : BunitContext, IAsyncLifetime
{
    private ScriptedChatClient? _chatClient;

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public EpicEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddDataProtection();
        Services.AddLogging();
        AddAuthorization().SetAuthorized("Djohnnie").SetClaims(new Claim(GitHubClaims.AccessToken, "token"));
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<RepositoryContext>();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton(TimeProvider.System);
        Services.AddApplication();
        Services.AddSingleton(_ => new AssistantAgentFactory(_chatClient, NullLoggerFactory.Instance));
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(new NoAgents()) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    [Fact]
    public async Task NewEpic_ProposesFirstTemplate_UsesTitleAsPlaceholder_AndSavesIndependentCopy()
    {
        await SelectRepositoryAsync();
        var templates = Services.GetRequiredService<EpicTemplateService>();
        var otherTemplate = await templates.SaveAsync(new EpicTemplate { Name = "Zebra", TitleTemplate = "Other example", Description = "Other" }, Xunit.TestContext.Current.CancellationToken);
        var template = await templates.SaveAsync(new EpicTemplate
        {
            Name = "Feature", TitleTemplate = "Add [capability]", Description = "## Goal\n\n## Acceptance criteria"
        }, Xunit.TestContext.Current.CancellationToken);
        Render<MudPopoverProvider>();
        var cut = Render<EpicEditor>();

        Assert.Equal(template.Id.ToString(), cut.FindComponent<MudSelect<int>>().Find("input").GetAttribute("value"));
        var title = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title");
        Assert.Equal("Add [capability]", title.Instance.Placeholder);
        Assert.Equal(string.Empty, title.Find("input").GetAttribute("value"));
        Assert.Equal(template.Description, cut.FindComponent<MarkdownEditor>().Instance.Value);
        title.Find("input").Input("Real epic title");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());
        var epic = Assert.Single(await Services.GetRequiredService<EpicService>().GetAllAsync("owner/repo", Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("Real epic title", epic.Title);
        Assert.Equal(template.Description, epic.Description);

        await templates.DeleteAsync(template.Id, Xunit.TestContext.Current.CancellationToken);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/epics/{epic.Id}?template={otherTemplate.Id}");
        var existing = Render<EpicEditor>(p => p.Add(e => e.Id, epic.Id));
        Assert.Equal(epic.Description, existing.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Empty(existing.FindComponents<MudSelect<int>>());
    }

    [Fact]
    public async Task NewEpic_ExplicitTemplateAndSwitching_PreserveTitleAndConfirmEditedDescription()
    {
        await SelectRepositoryAsync();
        var templates = Services.GetRequiredService<EpicTemplateService>();
        var first = await templates.SaveAsync(new EpicTemplate { Name = "A", TitleTemplate = "First", Description = "## First" }, Xunit.TestContext.Current.CancellationToken);
        var second = await templates.SaveAsync(new EpicTemplate { Name = "B", TitleTemplate = "Second", Description = "## Second" }, Xunit.TestContext.Current.CancellationToken);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/epics/new?template={second.Id}");
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var cut = Render<EpicEditor>();
        Assert.Equal(second.Description, cut.FindComponent<MarkdownEditor>().Instance.Value);
        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Keep my title");

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<int>>().Instance.SelectOption(value: first.Id));
        Assert.Equal(first.Description, cut.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Empty(dialogs.FindAll(".mud-dialog"));

        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("My edited description"));
        await cut.InvokeAsync(() => { _ = cut.FindComponent<MudSelect<int>>().Instance.SelectOption(value: second.Id); });
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Cancel").Click());
        Assert.Equal("My edited description", cut.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Equal(first.Id.ToString(), cut.FindComponent<MudSelect<int>>().Find("input").GetAttribute("value"));

        await cut.InvokeAsync(() => { _ = cut.FindComponent<MudSelect<int>>().Instance.SelectOption(value: second.Id); });
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Replace").Click());
        Assert.Equal(second.Description, cut.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Equal("Keep my title", cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").GetAttribute("value"));

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<int>>().Instance.SelectOption(value: 0));
        Assert.Empty(cut.FindComponent<MarkdownEditor>().Instance.Value!);
        Assert.Null(cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Instance.Placeholder);
    }

    [Fact]
    public async Task NewEpic_MissingRequestedTemplate_DoesNotSilentlyApplyAnother()
    {
        await SelectRepositoryAsync();
        await Services.GetRequiredService<EpicTemplateService>().SaveAsync(new EpicTemplate
        {
            Name = "Feature", TitleTemplate = "Example", Description = "## Goal"
        }, Xunit.TestContext.Current.CancellationToken);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/epics/new?template=999");
        Render<MudPopoverProvider>();
        var cut = Render<EpicEditor>();

        Assert.Empty(cut.FindComponent<MarkdownEditor>().Instance.Value!);
        Assert.Equal("0", cut.FindComponent<MudSelect<int>>().Find("input").GetAttribute("value"));
        Assert.Contains(Services.GetRequiredService<ISnackbar>().ShownSnackbars,
            snackbar => snackbar.Message?.ToString()?.Contains("no longer exists") == true);
    }

    [Fact]
    public async Task SaveDraft_NewEpic_StoresItAndOpensIt()
    {
        await SelectRepositoryAsync();
        Render<MudPopoverProvider>();
        var cut = Render<EpicEditor>();
        Assert.Contains("DGHSF Copilot is not configured yet", cut.Markup);

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Passkey login");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        var epic = Assert.Single(await Services.GetRequiredService<EpicService>().GetAllAsync("owner/repo", Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("Passkey login", epic.Title);
        Assert.Equal(WorkItemStatus.Draft, epic.Status);
        Assert.EndsWith($"/epics/{epic.Id}", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task AssignedEpic_IsReadOnlyAndLinksToIssue()
    {
        await SelectRepositoryAsync();
        var service = Services.GetRequiredService<EpicService>();
        var epic = await service.SaveAsync(new Epic { RepositoryFullName = "owner/repo", Title = "Done deal", Description = "Body" }, Xunit.TestContext.Current.CancellationToken);
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(Xunit.TestContext.Current.CancellationToken))
        {
            var entity = await db.Epics.SingleAsync(Xunit.TestContext.Current.CancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.IssueNumber = 7;
            entity.IssueUrl = "https://github.com/owner/repo/issues/7";
            await db.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
        }
        Render<MudPopoverProvider>();

        var cut = Render<EpicEditor>(p => p.Add(e => e.Id, epic.Id));

        cut.WaitForAssertion(() => Assert.Contains("https://github.com/owner/repo/issues/7", cut.Markup));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Accept") || b.TextContent.Contains("Save"));
    }

    [Fact]
    public async Task Assistant_PrefillsTheDraftFromThePrompt()
    {
        await SelectRepositoryAsync();
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("update_epic_form", new Dictionary<string, object?>
            {
                ["title"] = "Add passkey login",
                ["description"] = "## Goal\nSign in without passwords.",
                ["baseBranch"] = "develop",
                ["model"] = "unknown-model"
            }),
            ScriptedChatClient.Text("Filled in the epic."));
        Render<MudPopoverProvider>();
        var cut = Render<EpicEditor>();

        var chatInput = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Describe what you want to build").Find("input");
        chatInput.Input("Passkeys please");
        await cut.InvokeAsync(() => chatInput.KeyDown("Enter"));

        cut.WaitForAssertion(() => Assert.Contains("Filled in the epic.", cut.Markup), TimeSpan.FromSeconds(5));
        var toolResult = _chatClient.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().Single().Result?.ToString();
        Assert.Contains("Updated: title, description, base branch.", toolResult);
        Assert.Contains("Unknown model 'unknown-model'", toolResult);

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        var epic = Assert.Single(await Services.GetRequiredService<EpicService>().GetAllAsync("owner/repo", Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("Add passkey login", epic.Title);
        Assert.Equal("## Goal\nSign in without passwords.", epic.Description);
        Assert.Equal("develop", epic.BaseBranch);
        Assert.Null(epic.Model);
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));
    }

    private sealed class NoAgents : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
