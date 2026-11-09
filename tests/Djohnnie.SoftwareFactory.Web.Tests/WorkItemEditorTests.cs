using System.Net;
using System.Security.Claims;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
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
using MudBlazor.Extensions;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class WorkItemEditorTests : BunitContext, IAsyncLifetime
{
    private const string Repo = "owner/repo";
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;
    private ScriptedChatClient? _chatClient;

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public WorkItemEditorTests()
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

    private WorkItemService Items => Services.GetRequiredService<WorkItemService>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public async Task NewEpic_ProposesFirstTemplateOfItsType_UsesTitleAsPlaceholder_AndSavesIndependentCopy()
    {
        await SelectRepositoryAsync();
        var templates = Services.GetRequiredService<WorkItemTemplateService>();
        var otherTemplate = await templates.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Epic, Name = "Zebra", TitleTemplate = "Other example", Description = "Other" }, _cancellationToken);
        await templates.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Bug, Name = "A bug template", TitleTemplate = "Bug example", Description = "## Steps" }, _cancellationToken);
        var template = await templates.SaveAsync(new WorkItemTemplate
        {
            Type = WorkItemType.Epic, Name = "Feature", TitleTemplate = "Add [capability]", Description = "## Goal\n\n## Acceptance criteria"
        }, _cancellationToken);
        Navigation.NavigateTo("/epics/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        Assert.Equal(template.Id.ToString(), cut.FindComponent<MudSelect<int>>().Find("input").GetAttribute("value"));
        var title = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title");
        Assert.Equal("Add [capability]", title.Instance.Placeholder);
        Assert.Equal(string.Empty, title.Find("input").GetAttribute("value"));
        Assert.Equal(template.Description, cut.FindComponent<MarkdownEditor>().Instance.Value);
        title.Find("input").Input("Real epic title");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());
        var epic = Assert.Single(await Items.GetAllAsync(Repo, cancellationToken: _cancellationToken));
        Assert.Equal(WorkItemType.Epic, epic.Type);
        Assert.Equal("Real epic title", epic.Title);
        Assert.Equal(template.Description, epic.Description);

        await templates.DeleteAsync(template.Id, _cancellationToken);
        Navigation.NavigateTo($"/epics/{epic.Id}?template={otherTemplate.Id}");
        var existing = Render<WorkItemEditor>(p => p.Add(e => e.Id, epic.Id));
        Assert.Equal(epic.Description, existing.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Empty(existing.FindComponents<MudSelect<int>>());
    }

    [Fact]
    public async Task NewEpic_ExplicitTemplateAndSwitching_PreserveTitleAndConfirmEditedDescription()
    {
        await SelectRepositoryAsync();
        var templates = Services.GetRequiredService<WorkItemTemplateService>();
        var first = await templates.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Epic, Name = "A", TitleTemplate = "First", Description = "## First" }, _cancellationToken);
        var second = await templates.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Epic, Name = "B", TitleTemplate = "Second", Description = "## Second" }, _cancellationToken);
        Navigation.NavigateTo($"/epics/new?template={second.Id}");
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var cut = Render<WorkItemEditor>();
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
    public async Task Reset_LoadsTheStoredItemAfterConfirming_AndKeepsTheCopilotConversation()
    {
        await SelectRepositoryAsync();
        var epic = await Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Stored title", Description = "Stored" }, _cancellationToken);
        Navigation.NavigateTo($"/epics/{epic.Id}");
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, epic.Id));
        var chat = cut.FindComponent<AssistantChat>().Instance;
        Assert.Equal("/epics", cut.Find("a[aria-label='Back']").GetAttribute("href"));
        Assert.DoesNotContain(cut.FindAll("button, a"), b => b.TextContent.Trim() is "Back" or "Cancel");

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Edited title");
        await cut.InvokeAsync(() => { _ = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Reset").ClickAsync(new()); });
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Reset").Click());

        cut.WaitForAssertion(() => Assert.Equal("Stored title",
            cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").GetAttribute("value")));
        Assert.Same(chat, cut.FindComponent<AssistantChat>().Instance);
    }

    [Fact]
    public async Task Reset_IsNotOfferedOnANewItem()
    {
        await SelectRepositoryAsync();
        Navigation.NavigateTo("/epics/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Save draft"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Reset");
    }

    [Fact]
    public async Task NewEpic_MissingRequestedTemplate_DoesNotSilentlyApplyAnother()
    {
        await SelectRepositoryAsync();
        await Services.GetRequiredService<WorkItemTemplateService>().SaveAsync(new WorkItemTemplate
        {
            Type = WorkItemType.Epic, Name = "Feature", TitleTemplate = "Example", Description = "## Goal"
        }, _cancellationToken);
        Navigation.NavigateTo("/epics/new?template=999");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        Assert.Empty(cut.FindComponent<MarkdownEditor>().Instance.Value!);
        Assert.Equal("0", cut.FindComponent<MudSelect<int>>().Find("input").GetAttribute("value"));
        Assert.Contains(Services.GetRequiredService<ISnackbar>().ShownSnackbars,
            snackbar => snackbar.Message?.ToString()?.Contains("no longer exists") == true);
    }

    [Fact]
    public async Task SaveDraft_NewEpic_StoresItAndOpensIt_WithoutAssignmentSettings()
    {
        await SelectRepositoryAsync();
        Navigation.NavigateTo("/epics/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();
        Assert.Contains("DGHSF Copilot is not configured yet", cut.Markup);
        Assert.Empty(cut.FindComponents<AgentPicker>());
        Assert.Empty(cut.FindComponents<MudSelect<int?>>());
        Assert.Contains("Accept &amp; create issue", cut.Markup);

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Passkey login");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        var epic = Assert.Single(await Items.GetAllAsync(Repo, cancellationToken: _cancellationToken));
        Assert.Equal("Passkey login", epic.Title);
        Assert.Equal(WorkItemStatus.Draft, epic.Status);
        Assert.EndsWith($"/epics/{epic.Id}", Navigation.Uri);
    }

    [Fact]
    public async Task NewStory_FromParentLink_PrefillsTheFeature_ShowsAssignment_AndSavesUnderIt()
    {
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Passkeys");
        var feature = await SaveAsync(WorkItemType.Feature, "Passkey login", epic.Id);
        var otherFeature = await SaveAsync(WorkItemType.Feature, "Another feature", epic.Id);
        Navigation.NavigateTo($"/stories/new?parent={feature.Id}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        var parent = cut.FindComponent<MudSelect<int?>>();
        Assert.Equal(feature.Id, parent.Instance.GetState(x => x.Value));
        Assert.True(parent.Instance.Required);
        Assert.Single(cut.FindComponents<AgentPicker>());
        Assert.Contains("Accept &amp; assign", cut.Markup);
        Assert.Contains("isn't accepted yet", cut.Markup);

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Sign in with a passkey");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        var story = Assert.Single(await Items.GetAllAsync(Repo, [WorkItemType.Story], cancellationToken: _cancellationToken));
        Assert.Equal(feature.Id, story.ParentId);
        Assert.Empty(await Items.GetAllAsync(Repo, parentId: otherFeature.Id, cancellationToken: _cancellationToken));
        Assert.EndsWith($"/stories/{story.Id}", Navigation.Uri);
    }

    [Fact]
    public async Task NewStory_WithoutParent_IsNotSaved()
    {
        await SelectRepositoryAsync();
        Navigation.NavigateTo("/stories/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        Assert.Contains("There is no feature yet in this repository.", cut.Markup);
        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Orphan story");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        Assert.Empty(await Items.GetAllAsync(Repo, cancellationToken: _cancellationToken));
    }

    [Fact]
    public async Task Epic_ListsItsChildren_AndLinksToAddFeaturesBugsAndTasks()
    {
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Passkeys");
        var feature = await SaveAsync(WorkItemType.Feature, "Passkey login", epic.Id);
        await SaveAsync(WorkItemType.Bug, "Login button is hidden", epic.Id);
        Navigation.NavigateTo($"/epics/{epic.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, epic.Id));

        var children = cut.Find("[aria-label='Children']");
        Assert.Contains("Passkey login", children.TextContent);
        Assert.Contains("Login button is hidden", children.TextContent);
        Assert.Contains(children.QuerySelectorAll("a"), a => a.GetAttribute("href") == $"/features/{feature.Id}");
        Assert.Equal(
            [$"/features/new?parent={epic.Id}", $"/bugs/new?parent={epic.Id}", $"/tasks/new?parent={epic.Id}"],
            children.QuerySelectorAll("a").Select(a => a.GetAttribute("href")).Where(href => href!.Contains("/new")));
    }

    [Fact]
    public async Task ItemOpenedOnThePageOfAnotherType_RedirectsToItsOwnType()
    {
        await SelectRepositoryAsync();
        var bug = await SaveAsync(WorkItemType.Bug, "Bug");
        Navigation.NavigateTo($"/epics/{bug.Id}");

        Render<WorkItemEditor>(p => p.Add(e => e.Id, bug.Id));

        Assert.EndsWith($"/bugs/{bug.Id}", Navigation.Uri);
    }

    [Fact]
    public async Task AssignedTask_IsReadOnlyAndLinksToIssue()
    {
        await SelectRepositoryAsync();
        var task = await SaveAsync(WorkItemType.Task, "Done deal");
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.IssueNumber = 7;
            entity.IssueUrl = "https://github.com/owner/repo/issues/7";
            await db.SaveChangesAsync(_cancellationToken);
        }
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        cut.WaitForAssertion(() => Assert.Contains("https://github.com/owner/repo/issues/7", cut.Markup));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Accept") || b.TextContent.Contains("Save"));
    }

    [Fact]
    public async Task List_OnlyShowsItemsOfTheRouteType_WithTheirParent()
    {
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Passkeys");
        await SaveAsync(WorkItemType.Bug, "Child bug", epic.Id);
        await SaveAsync(WorkItemType.Bug, "Free bug");
        Navigation.NavigateTo("/bugs");

        var cut = Render<WorkItems>();

        cut.WaitForAssertion(() => Assert.Contains("Child bug", cut.Markup));
        Assert.Contains("Free bug", cut.Markup);
        Assert.Contains("Standalone", cut.Markup);
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == $"/epics/{epic.Id}");
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/bugs/new");
        Assert.Equal(["Bug", "Part of", "Status", "Issue", "Agent", "Model", "Updated", "Actions"], cut.FindAll("th").Select(th => th.TextContent.Trim()));

        Navigation.NavigateTo("/epics");
        var epics = Render<WorkItems>();
        epics.WaitForAssertion(() => Assert.Contains("Passkeys", epics.Markup));
        Assert.DoesNotContain("Child bug", epics.Markup);
        Assert.Equal(["Epic", "Status", "Issue", "Updated", "Actions"], epics.FindAll("th").Select(th => th.TextContent.Trim()));
    }

    [Fact]
    public void FormTools_OnlyOfferAssignmentToolsForAssignableTypes()
    {
        static IEnumerable<string> ToolNames(WorkItemType type)
        {
            var tools = new WorkItemFormTools(() => new WorkItem { Type = type }, () => null, [], _ => Task.FromResult(new List<Contracts.Agents.AgentSummary>()), change =>
            {
                change();
                return Task.CompletedTask;
            }).AsTools();
            var property = typeof(AssistantTools).GetProperty("Tools", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            return ((IList<Microsoft.Extensions.AI.AITool>)property.GetValue(tools)!).Select(t => t.Name);
        }

        Assert.Equal(["get_work_item_form", "get_selected_template", "update_work_item_form"], ToolNames(WorkItemType.Epic));
        Assert.Equal(["get_work_item_form", "get_selected_template", "update_work_item_form"], ToolNames(WorkItemType.Feature));
        Assert.Equal(["get_work_item_form", "get_selected_template", "update_work_item_form", "list_custom_agents", "list_models"], ToolNames(WorkItemType.Bug));
        Assert.Contains("labelled `story`", WorkItemFormTools.InstructionsFor(WorkItemType.Story));
        Assert.Contains("never assigned", WorkItemFormTools.InstructionsFor(WorkItemType.Feature));
    }

    [Theory]
    [InlineData(WorkItemType.Epic)]
    [InlineData(WorkItemType.Feature)]
    [InlineData(WorkItemType.Story)]
    [InlineData(WorkItemType.Bug)]
    [InlineData(WorkItemType.Task)]
    public void Instructions_LeaveTheStructureToTheSelectedTemplate(WorkItemType type)
    {
        var instructions = WorkItemFormTools.InstructionsFor(type);

        Assert.Contains("`get_selected_template`", instructions);
        Assert.DoesNotContain("## ", instructions.Replace("## Your task", string.Empty));
    }

    [Fact]
    public async Task Assistant_GetsTheSelectedTemplateAndCurrentFormWithEveryRequest()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        await Services.GetRequiredService<WorkItemTemplateService>().SaveAsync(new WorkItemTemplate
        {
            Type = WorkItemType.Epic, Name = "Initiative", TitleTemplate = "Launch [capability]", Description = "## Outcome\n<!-- What changes? -->"
        }, _cancellationToken);
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("get_selected_template", new Dictionary<string, object?>()),
            ScriptedChatClient.Text("Using the template."),
            ScriptedChatClient.Text("Still using it."));
        Navigation.NavigateTo("/epics/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Launch passkeys");
        await SendToCopilotAsync(cut, "Add passkeys");
        cut.WaitForAssertion(() => Assert.Contains("Using the template.", cut.Markup), TimeSpan.FromSeconds(5));

        var first = _chatClient.Requests[0].Last(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Text;
        Assert.Contains("\"name\": \"Initiative\"", first);
        Assert.Contains("\"titleExample\": \"Launch [capability]\"", first);
        Assert.Contains("<!-- What changes? -->", first);
        Assert.Contains("\"title\": \"Launch passkeys\"", first);
        Assert.EndsWith("The user's message:\nAdd passkeys", first);
        var toolResult = _chatClient.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().Single().Result?.ToString();
        Assert.Contains("Initiative", toolResult);

        // The next request carries the form as it is by then.
        var editor = cut.FindComponent<MarkdownEditor>();
        await cut.InvokeAsync(() => editor.Instance.ValueChanged.InvokeAsync("## Outcome\nSign in without a password."));
        await SendToCopilotAsync(cut, "Shorter please");
        cut.WaitForAssertion(() => Assert.Contains("Still using it.", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("Sign in without a password.", _chatClient.Requests[2].Last(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Text);
    }

    [Fact]
    public async Task Assistant_IsToldWhenNoTemplateIsSelected()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("No template."));
        Navigation.NavigateTo("/features/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        await SendToCopilotAsync(cut, "Add passkeys");
        cut.WaitForAssertion(() => Assert.Contains("No template.", cut.Markup), TimeSpan.FromSeconds(5));

        var request = Assert.Single(_chatClient.Requests).Last(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Text;
        Assert.Contains("\"selected\": false", request);
        Assert.DoesNotContain("descriptionSkeleton", request);
    }

    private static async Task SendToCopilotAsync(IRenderedComponent<WorkItemEditor> cut, string text)
    {
        var input = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Describe what you want to build").Find("input");
        input.Input(text);
        await cut.InvokeAsync(() => input.KeyDown("Enter"));
    }

    [Fact]
    public async Task Assistant_PrefillsTheDraftFromThePrompt()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("update_work_item_form", new Dictionary<string, object?>
            {
                ["title"] = "Upgrade to .NET 10",
                ["description"] = "## Goal\nRun on the latest runtime.",
                ["baseBranch"] = "develop",
                ["model"] = "unknown-model"
            }),
            ScriptedChatClient.Text("Filled in the task."));
        Navigation.NavigateTo("/tasks/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        var chatInput = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Describe what you want to build").Find("input");
        chatInput.Input("Upgrade please");
        await cut.InvokeAsync(() => chatInput.KeyDown("Enter"));

        cut.WaitForAssertion(() => Assert.Contains("Filled in the task.", cut.Markup), TimeSpan.FromSeconds(5));
        var toolResult = _chatClient.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().Single().Result?.ToString();
        Assert.Contains("Updated: title, description, base branch.", toolResult);
        Assert.Contains("Unknown model 'unknown-model'", toolResult);

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        var task = Assert.Single(await Items.GetAllAsync(Repo, cancellationToken: _cancellationToken));
        Assert.Equal(WorkItemType.Task, task.Type);
        Assert.Equal("Upgrade to .NET 10", task.Title);
        Assert.Equal("## Goal\nRun on the latest runtime.", task.Description);
        Assert.Equal("develop", task.BaseBranch);
        Assert.Null(task.Model);
    }

    [Fact]
    public async Task ModelSuggestion_IsDebouncedOnDescriptionChanges_AndAppliedFromTheChip()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        var models = Services.GetRequiredService<Application.Features.AiModels.AiModelService>();
        await models.SaveAsync(new Contracts.AiModels.AiModel { Name = "GPT-5", ModelId = "gpt-5" }, _cancellationToken);
        await models.SaveAsync(new Contracts.AiModels.AiModel { Name = "GPT-5 mini", ModelId = "gpt-5-mini" }, _cancellationToken);
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("""{"modelId":"gpt-5-mini","complexity":"low","reason":"A typo fix."}"""));
        Navigation.NavigateTo("/tasks/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();
        cut.WaitForAssertion(() => Assert.Contains("Assignment", cut.Markup));

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Typo");
        var editor = cut.FindComponent<MarkdownEditor>();
        await cut.InvokeAsync(() => editor.Instance.ValueChanged.InvokeAsync("Fix"));
        await Task.Delay(TimeSpan.FromSeconds(1), _cancellationToken);
        await cut.InvokeAsync(() => editor.Instance.ValueChanged.InvokeAsync("Fix the typo on the home page"));
        await Task.Delay(TimeSpan.FromSeconds(2.5), _cancellationToken);
        Assert.Empty(_chatClient.Requests);

        cut.WaitForAssertion(() => Assert.Contains("Suggestion: GPT-5 mini · low", cut.Markup), TimeSpan.FromSeconds(5));
        var request = Assert.Single(_chatClient.Requests);
        Assert.Contains("Fix the typo on the home page", string.Join("\n", request.Select(m => m.Text)));
        Assert.Null(cut.FindComponent<ModelPicker>().Instance.Value);

        await cut.InvokeAsync(() => cut.FindComponents<MudChip<string>>().Single(c => c.Markup.Contains("GPT-5 mini")).Find(".mud-chip").Click());

        Assert.Equal("gpt-5-mini", cut.FindComponent<ModelPicker>().Instance.Value);
        Assert.Contains("Suggested: GPT-5 mini · low", cut.Markup);
        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Typo fix");
        await Task.Delay(TimeSpan.FromSeconds(3.5), _cancellationToken);
        Assert.Single(_chatClient.Requests);
    }

    [Fact]
    public async Task ChatSuggestions_RefreshWhenAChangedTitleOrDescriptionLosesFocus()
    {
        await SelectRepositoryAsync();
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.Text("""{"suggestions":["First idea"]}"""),
            ScriptedChatClient.Text("""{"suggestions":["About the title"]}"""),
            ScriptedChatClient.Text("""{"suggestions":["About the description"]}"""));
        Navigation.NavigateTo("/tasks/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();
        cut.WaitForAssertion(() => Assert.Contains("First idea", cut.Markup), TimeSpan.FromSeconds(5));

        var title = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input");
        title.Input("Upgrade .NET");
        Assert.Single(_chatClient.Requests);
        await cut.InvokeAsync(() => title.Blur());
        cut.WaitForAssertion(() => Assert.Contains("About the title", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("Upgrade .NET", string.Join("\n", _chatClient.Requests[1].Select(m => m.Text)));

        // Losing focus without a change doesn't ask again.
        await cut.InvokeAsync(() => title.Blur());
        var editor = cut.FindComponent<MarkdownEditor>();
        await cut.InvokeAsync(() => editor.Instance.OnBlur.InvokeAsync());
        Assert.Equal(2, _chatClient.Requests.Count);

        await cut.InvokeAsync(() => editor.Instance.ValueChanged.InvokeAsync("## Goal\nRun on .NET 10."));
        Assert.Equal(2, _chatClient.Requests.Count);
        await cut.InvokeAsync(() => editor.Instance.OnBlur.InvokeAsync());
        cut.WaitForAssertion(() => Assert.Contains("About the description", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("Run on .NET 10.", string.Join("\n", _chatClient.Requests[2].Select(m => m.Text)));
    }

    [Fact]
    public async Task ModelSuggestion_ForAnExistingDraftWithADescription_IsShownWhenThePageOpens()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        await Services.GetRequiredService<Application.Features.AiModels.AiModelService>()
            .SaveAsync(new Contracts.AiModels.AiModel { Name = "GPT-5", ModelId = "gpt-5" }, _cancellationToken);
        var bug = await Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Race", Description = "Orders are lost under load." }, _cancellationToken);
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("""{"modelId":"gpt-5","complexity":"high","reason":"Concurrency bug."}"""));
        Navigation.NavigateTo($"/bugs/{bug.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, bug.Id));

        cut.WaitForAssertion(() => Assert.Contains("Suggestion: GPT-5 · high", cut.Markup), TimeSpan.FromSeconds(2));
        Assert.Contains("Orders are lost under load.", string.Join("\n", Assert.Single(_chatClient.Requests).Select(m => m.Text)));
        Assert.Null(cut.FindComponent<ModelPicker>().Instance.Value);
    }

    private Task<WorkItem> SaveAsync(WorkItemType type, string title, int? parentId = null)
    {
        return Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = type, ParentId = parentId, Title = title, Description = "Body" }, _cancellationToken);
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", Repo, new GitHubOwner("owner", null), "https://github.com/owner/repo",
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
