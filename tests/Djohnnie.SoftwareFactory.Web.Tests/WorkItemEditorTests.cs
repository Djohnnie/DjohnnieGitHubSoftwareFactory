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
using Microsoft.AspNetCore.Components.Routing;
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
    private readonly NoAgents _gitHub = new();

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
            new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") },
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

        Assert.Equal(template.Id.ToString(), cut.FindComponent<MudSelect<Guid>>().Find("input").GetAttribute("value"));
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
        Assert.Empty(existing.FindComponents<MudSelect<Guid>>());
    }

    [Fact]
    public async Task NewItemForAMindmapIdea_WithoutTheWriter_UsesTheIdeaAsTitle_AndLinksTheIdeaOnSave()
    {
        await SelectRepositoryAsync();
        var mindmaps = Services.GetRequiredService<Application.Features.Mindmaps.MindmapService>();
        var epic = await Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Security", Description = "Safer." }, _cancellationToken);
        var map = Contracts.Mindmaps.Mindmap.Create(Repo, "Release 2.0");
        var security = MindmapLayout.AddChild(map, map.Root!.Id, "Security");
        security.WorkItemId = epic.Id;
        var passkeys = MindmapLayout.AddChild(map, security.Id, "Passkey login");
        map = await mindmaps.SaveAsync(map, _cancellationToken);
        var parent = MindmapLayout.ProposedParent(map, passkeys.Id, WorkItemType.Feature, new Dictionary<Guid, WorkItem> { [epic.Id] = epic });
        Assert.Equal(epic.Id, parent);
        Navigation.NavigateTo(WorkItemRoutes.NewPath(WorkItemType.Feature, parent, mindmapId: map.Id, ideaId: passkeys.Id));
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>();

        cut.WaitForAssertion(() => Assert.Contains("Passkey login", cut.Find(".mindmap-origin").TextContent));
        Assert.Contains("Release 2.0", cut.Find(".mindmap-origin").TextContent);
        Assert.DoesNotContain("Write again", cut.Find(".mindmap-origin").TextContent);
        Assert.Equal("Passkey login", cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").GetAttribute("value"));

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        var feature = Assert.Single(await Items.GetAllAsync(Repo, [WorkItemType.Feature], cancellationToken: _cancellationToken));
        Assert.Equal(("Passkey login", epic.Id), (feature.Title, feature.ParentId));
        Assert.Equal(feature.Id, (await mindmaps.GetAsync(Repo, map.Id, _cancellationToken))!.Find(passkeys.Id)!.WorkItemId);

        // The stored item links back to the idea.
        Navigation.NavigateTo(WorkItemRoutes.ItemPath(WorkItemType.Feature, feature.Id));
        var stored = Render<WorkItemEditor>(p => p.Add(e => e.Id, feature.Id));
        var chip = stored.WaitForElement(".mindmap-idea-chip");
        Assert.Contains("Release 2.0 › Passkey login", chip.TextContent);
        Assert.EndsWith($"/mindmaps/{map.Id}?idea={passkeys.Id}", (chip.GetAttribute("href") ?? chip.QuerySelector("a")?.GetAttribute("href"))!);
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

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<Guid>>().Instance.SelectOption(value: first.Id));
        Assert.Equal(first.Description, cut.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Empty(dialogs.FindAll(".mud-dialog"));

        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("My edited description"));
        await cut.InvokeAsync(() => { _ = cut.FindComponent<MudSelect<Guid>>().Instance.SelectOption(value: second.Id); });
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Cancel").Click());
        Assert.Equal("My edited description", cut.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Equal(first.Id.ToString(), cut.FindComponent<MudSelect<Guid>>().Find("input").GetAttribute("value"));

        await cut.InvokeAsync(() => { _ = cut.FindComponent<MudSelect<Guid>>().Instance.SelectOption(value: second.Id); });
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Replace").Click());
        Assert.Equal(second.Description, cut.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Equal("Keep my title", cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").GetAttribute("value"));

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<Guid>>().Instance.SelectOption(value: Guid.Empty));
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
        Navigation.NavigateTo($"/epics/new?template={Guid.NewGuid()}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        Assert.Empty(cut.FindComponent<MarkdownEditor>().Instance.Value!);
        Assert.Equal(Guid.Empty.ToString(), cut.FindComponent<MudSelect<Guid>>().Find("input").GetAttribute("value"));
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
        Assert.Contains("DGHSF Agent is not configured yet", cut.Markup);
        Assert.Empty(cut.FindComponents<AgentPicker>());
        Assert.Empty(cut.FindComponents<MudSelect<Guid?>>());
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

        var parent = cut.FindComponent<MudSelect<Guid?>>();
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
    public async Task TaskOnTheWorker_ShowsItsRunsWithTheirCost_AndStartsAnotherRunAfterAFailedOne()
    {
        await SelectRepositoryAsync();
        var task = await Items.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Worker task", Description = "About it.", Executor = AgentExecutor.DghsfWorker
        }, _cancellationToken);
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.IssueNumber = 7;
            entity.IssueUrl = "https://github.com/owner/repo/issues/7";
            entity.AssignedAt = DateTimeOffset.UtcNow.AddMinutes(-31);
            db.AgentRuns.Add(new DataAccess.Sql.Entities.AgentRun
            {
                Id = Guid.NewGuid(), WorkItemId = task.Id, RepositoryFullName = Repo, Status = Contracts.AgentRuns.AgentRunStatus.Failed,
                Attempts = 1, MaxAttempts = 2, NanoAiu = 2_500_000_000, UsageComplete = true, Models = "claude-sonnet-4.5",
                Error = "The agent finished without changing any files.", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-29), CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
            });
            await db.SaveChangesAsync(_cancellationToken);
        }
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        cut.WaitForAssertion(() => Assert.Contains("Agent worker runs", cut.Markup));
        var panel = cut.FindComponent<AgentRunsPanel>();
        Assert.Contains("1 run · 3 AI credits", panel.Markup);
        Assert.Contains("claude-sonnet-4.5", panel.Markup);
        Assert.Contains("without changing any files", panel.Markup);
        Assert.Contains("9 min", panel.Markup);
        Assert.Contains("Handed to the DGHSF agent worker", cut.Markup);
        // Starting over first refreshes the item from its issue, because the worker reads it from the database.
        _gitHub.IssueJson = """{"id":1007,"number":7,"title":"Renamed on GitHub","html_url":"https://github.com/owner/repo/issues/7","state":"open","body":"Edited","assignees":[]}""";

        await cut.InvokeAsync(() => panel.FindAll("button").Single(b => b.TextContent.Contains("Start over")).Click());

        panel.WaitForAssertion(() => Assert.Contains("2 runs", panel.Markup));
        Assert.Contains("Waiting", panel.Markup);
        Assert.Contains(panel.FindAll("button"), b => b.TextContent.Contains("Cancel run"));
        Assert.DoesNotContain(panel.FindAll("button"), b => b.TextContent.Contains("Start over"));
        Assert.Equal("Renamed on GitHub", (await Items.GetAsync(Repo, task.Id, _cancellationToken))!.Title);
    }

    [Fact]
    public async Task TaskOnTheWorker_WhileARunGoesOn_ShowsThatRunInsteadOfTheTask()
    {
        await SelectRepositoryAsync();
        var task = await Items.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Worker task", Description = "About it.", Executor = AgentExecutor.DghsfWorker
        }, _cancellationToken);
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.IssueNumber = 7;
            entity.IssueUrl = "https://github.com/owner/repo/issues/7";
            entity.AssignedAt = DateTimeOffset.UtcNow.AddDays(-3);
            var run = new DataAccess.Sql.Entities.AgentRun
            {
                Id = Guid.NewGuid(), WorkItemId = task.Id, RepositoryFullName = Repo, Status = Contracts.AgentRuns.AgentRunStatus.Running,
                Kind = Contracts.AgentRuns.AgentRunKind.Revise, Attempts = 2, MaxAttempts = 3, NanoAiu = 2_500_000_000, UsageComplete = true,
                Models = "claude-sonnet-4.5", RequestedModel = "gpt-5", CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), StartedAt = DateTimeOffset.UtcNow.AddDays(-3)
            };
            db.AgentRuns.Add(run);
            db.AgentRunAttempts.Add(new DataAccess.Sql.Entities.AgentRunAttempt
            {
                Id = Guid.NewGuid(), AgentRunId = run.Id, Attempt = 1, Status = Contracts.AgentRuns.AgentRunStatus.Succeeded, NanoAiu = 2_500_000_000,
                Models = "claude-sonnet-4.5", UsageComplete = true, StartedAt = DateTimeOffset.UtcNow.AddDays(-3), CompletedAt = DateTimeOffset.UtcNow.AddDays(-3).AddMinutes(9)
            });
            db.AgentRunAttempts.Add(new DataAccess.Sql.Entities.AgentRunAttempt
            {
                Id = Guid.NewGuid(), AgentRunId = run.Id, Attempt = 2, Kind = Contracts.AgentRuns.AgentRunKind.Revise,
                Status = Contracts.AgentRuns.AgentRunStatus.Running, UsageComplete = true, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5).AddSeconds(-30)
            });
            await db.SaveChangesAsync(_cancellationToken);
        }
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        cut.WaitForAssertion(() => Assert.Contains("Agent worker runs", cut.Markup));
        var panel = cut.FindComponent<AgentRunsPanel>();
        var row = panel.Find(".agent-run");
        Assert.Contains("5 minutes ago", row.TextContent);
        Assert.DoesNotContain("3 days", row.TextContent);
        // The run that goes on costs nothing yet: it shows the model it asked for, not the totals of the runs before it.
        Assert.Contains("gpt-5", row.TextContent);
        Assert.DoesNotContain("claude-sonnet-4.5", row.TextContent);
        Assert.Contains("2 runs · 3 AI credits", panel.Markup);
    }

    [Fact]
    public async Task TaskOnTheWorker_ListsItsRunsOnTheAgentRunsTab_BeforeThePullRequestTabs()
    {
        await SelectRepositoryAsync();
        var task = await Items.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Worker task", Description = "About it.", Executor = AgentExecutor.DghsfWorker
        }, _cancellationToken);
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.IssueNumber = 7;
            entity.IssueUrl = "https://github.com/owner/repo/issues/7";
            entity.PullRequestNumber = 12;
            entity.PullRequestUrl = "https://github.com/owner/repo/pull/12";
            var run = new DataAccess.Sql.Entities.AgentRun
            {
                Id = Guid.NewGuid(), WorkItemId = task.Id, RepositoryFullName = Repo, Status = Contracts.AgentRuns.AgentRunStatus.Succeeded,
                Kind = Contracts.AgentRuns.AgentRunKind.Revise, Attempts = 2, MaxAttempts = 3, NanoAiu = 2_500_000_000, UsageComplete = true,
                PullRequestNumber = 12, PullRequestUrl = "https://github.com/owner/repo/pull/12",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2), StartedAt = DateTimeOffset.UtcNow.AddHours(-2), CompletedAt = DateTimeOffset.UtcNow.AddHours(-1)
            };
            db.AgentRuns.Add(run);
            db.AgentRunAttempts.Add(new DataAccess.Sql.Entities.AgentRunAttempt
            {
                Id = Guid.NewGuid(), AgentRunId = run.Id, Attempt = 1, Status = Contracts.AgentRuns.AgentRunStatus.Succeeded, NanoAiu = 1_000_000_000,
                Models = "claude-sonnet-4.5", PullRequestNumber = 12, PullRequestUrl = "https://github.com/owner/repo/pull/12", UsageComplete = true,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-2), CompletedAt = DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(9)
            });
            db.AgentRunAttempts.Add(new DataAccess.Sql.Entities.AgentRunAttempt
            {
                Id = Guid.NewGuid(), AgentRunId = run.Id, Attempt = 2, Kind = Contracts.AgentRuns.AgentRunKind.Revise,
                Status = Contracts.AgentRuns.AgentRunStatus.Succeeded, NanoAiu = 1_500_000_000, UsageComplete = true,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(-4), CompletedAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync(_cancellationToken);
        }
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".mud-tab"), t => t.TextContent.Contains("Agent runs")));
        var tabs = cut.FindAll(".mud-tab").Select(t => t.TextContent).ToList();
        Assert.Equal(4, tabs.Count);
        Assert.Contains("Agent runs", tabs[1]);
        Assert.Contains("2", tabs[1]);
        Assert.Contains("Pull request #12", tabs[2]);
        Assert.Contains("Discussion", tabs[3]);

        await cut.InvokeAsync(() => cut.FindAll(".mud-tab")[1].Click());

        // The whole tab is the item's tasks: not collapsible, and without the item's own title.
        var card = cut.Find(".agent-task");
        Assert.Empty(card.QuerySelectorAll(".run-toggle"));
        Assert.DoesNotContain("Worker task", card.TextContent);
        Assert.Contains("3 AI credits", card.TextContent);
        var rows = cut.FindComponent<MudTable<Contracts.AgentRuns.AgentRunAttempt>>().FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Revision", rows[0].TextContent);
        Assert.Contains("Implementation", rows[1].TextContent);
        Assert.Contains("claude-sonnet-4.5", rows[1].TextContent);
        Assert.Contains("9 min", rows[1].TextContent);

        // The pull request's tabs still open their own section.
        await cut.InvokeAsync(() => cut.FindAll(".mud-tab").Single(t => t.TextContent.Contains("Discussion")).Click());
        Assert.Equal(3, cut.FindComponent<MudTabs>().Instance.ActivePanelIndex);
    }

    [Fact]
    public async Task TaskOnTheWorker_WithoutRuns_HasNoAgentRunsTab()
    {
        await SelectRepositoryAsync();
        var task = await SaveAcceptedTaskAsync();
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.Executor = AgentExecutor.DghsfWorker;
            entity.PullRequestNumber = 12;
            entity.PullRequestUrl = "https://github.com/owner/repo/pull/12";
            await db.SaveChangesAsync(_cancellationToken);
        }
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".mud-tab"), t => t.TextContent.Contains("Pull request #12")));
        Assert.Equal(3, cut.FindAll(".mud-tab").Count);
        Assert.DoesNotContain(cut.FindAll(".mud-tab"), t => t.TextContent.Contains("Agent runs"));
    }

    [Fact]
    public async Task Draft_HasNoRefreshButtons()
    {
        await SelectRepositoryAsync();
        var task = await SaveAsync(WorkItemType.Task, "Draft");
        Render<MudPopoverProvider>();
        Navigation.NavigateTo($"/tasks/{task.Id}");
        var editor = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));
        editor.WaitForAssertion(() => Assert.Contains(editor.FindAll("button"), b => b.TextContent.Contains("Save draft")));
        Navigation.NavigateTo("/tasks");
        var list = Render<WorkItems>();
        list.WaitForAssertion(() => Assert.Contains("Draft", list.Markup));

        Assert.DoesNotContain(editor.FindAll("button"), b => b.TextContent.Contains("Refresh from GitHub"));
        Assert.Empty(list.FindAll("button[aria-label='Refresh from GitHub']"));
    }

    [Fact]
    public async Task TaskOnTheWorker_IncludesScreenshotsByDefault_AndSavesTurningThemOff()
    {
        await SelectRepositoryAsync();
        var task = await Items.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Task, Title = "Worker task", Description = "About it.", Executor = AgentExecutor.DghsfWorker
        }, _cancellationToken);
        Assert.True(task.IncludeScreenshots);
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        cut.WaitForAssertion(() => Assert.Contains("Include UI screenshots", cut.Markup));
        var screenshots = cut.FindComponents<MudSwitch<bool>>().Single(s => s.Instance.Label == "Include UI screenshots");
        Assert.True(screenshots.Instance.GetState(x => x.Value));
        await cut.InvokeAsync(() => screenshots.Find("input").Change(false));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        Assert.False((await Items.GetAsync(Repo, task.Id, _cancellationToken))!.IncludeScreenshots);
    }

    [Fact]
    public async Task Refresh_OnTheEditor_ShowsTheIssueAndKeepsUnsavedAssignmentSettings()
    {
        await SelectRepositoryAsync();
        var task = await SaveAcceptedTaskAsync();
        _gitHub.IssueJson = """{"id":1007,"number":7,"title":"Renamed on GitHub","html_url":"https://github.com/owner/repo/issues/7","state":"open","body":"Edited","assignees":[]}""";
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Refresh from GitHub")));
        await cut.InvokeAsync(() => cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Base branch").Find("input").Change("release"));

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Refresh from GitHub")).Click());

        cut.WaitForAssertion(() => Assert.Contains("Renamed on GitHub", cut.Find("h4").TextContent));
        Assert.Equal("release", cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Base branch").Find("input").GetAttribute("value"));
        var stored = await Items.GetAsync(Repo, task.Id, _cancellationToken);
        Assert.Equal("Renamed on GitHub", stored!.Title);
        Assert.Equal("Edited", stored.Description);
        Assert.Null(stored.BaseBranch);
    }

    [Fact]
    public async Task Refresh_OnTheList_ReloadsTheStoredItem()
    {
        await SelectRepositoryAsync();
        await SaveAcceptedTaskAsync();
        _gitHub.IssueJson = """{"id":1007,"number":7,"title":"Renamed on GitHub","html_url":"https://github.com/owner/repo/issues/7","state":"closed","body":"Edited","assignees":[]}""";
        Navigation.NavigateTo("/tasks");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItems>();
        cut.WaitForAssertion(() => Assert.Contains("Accepted", cut.Markup));

        await cut.InvokeAsync(() => cut.Find("button[aria-label='Refresh from GitHub']").Click());

        cut.WaitForAssertion(() => Assert.Contains("Renamed on GitHub", cut.Markup));
        Assert.Contains("Closed", cut.Markup);
    }

    private async Task<WorkItem> SaveAcceptedTaskAsync()
    {
        var task = await SaveAsync(WorkItemType.Task, "Accepted task");
        await using var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken);
        var entity = await db.WorkItems.SingleAsync(_cancellationToken);
        entity.Status = WorkItemStatus.Accepted;
        entity.IssueNumber = 7;
        entity.IssueUrl = "https://github.com/owner/repo/issues/7";
        await db.SaveChangesAsync(_cancellationToken);
        return task;
    }

    [Fact]
    public async Task PullRequest_ShowsTheDiff_AndSendsLineCommentsToCopilotAsOneReview()
    {
        await SelectRepositoryAsync();
        var task = await SaveAcceptedTaskAsync();
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.Progress = CopilotProgress.ReadyForReview;
            entity.PullRequestNumber = 12;
            entity.PullRequestUrl = "https://github.com/owner/repo/pull/12";
            await db.SaveChangesAsync(_cancellationToken);
        }
        _gitHub.Responses["GET /repos/owner/repo/pulls/12"] = """
            {"number":12,"title":"Fix the task","html_url":"https://github.com/owner/repo/pull/12","state":"open","draft":true,
             "user":{"login":"Copilot"},"merged_at":null,"additions":1,"deletions":1,"changed_files":1}
            """;
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/files"] = """
            [{"filename":"src/App.cs","status":"modified","additions":1,"deletions":1,"patch":"@@ -1,2 +1,2 @@\n-var old = 1;\n+var renamed = 1;\n return;"}]
            """;
        _gitHub.Responses["GET /repos/owner/repo/issues/12/comments"] = "[]";
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/comments"] = "[]";
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/reviews"] = "[]";
        _gitHub.Responses["POST /repos/owner/repo/pulls/12/reviews"] = """{"id":3,"html_url":"https://github.com/owner/repo/pull/12#pullrequestreview-3"}""";
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".mud-tab"), t => t.TextContent.Contains("Pull request #12")));
        Assert.Contains("Ready for review", cut.Markup);
        // The pull request is only loaded when its tab is opened.
        Assert.DoesNotContain(_gitHub.Bodies.Keys, k => k.StartsWith("GET /repos/owner/repo/pulls/12"));
        Assert.DoesNotContain("var renamed = 1;", cut.Markup);

        await cut.InvokeAsync(() => cut.FindAll(".mud-tab").Single(t => t.TextContent.Contains("Pull request #12")).Click());
        cut.WaitForAssertion(() => Assert.Contains("var renamed = 1;", cut.Markup));

        await cut.InvokeAsync(() => cut.Find("button[aria-label='Comment on line 1'].pr-add").Click());
        await cut.InvokeAsync(() => cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Comment on line 1").Find("textarea").Input("Use a clearer name"));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add comment").Click());
        cut.WaitForAssertion(() => Assert.Contains("1 pending", cut.Markup));
        await cut.InvokeAsync(() => cut.FindAll(".mud-tab").Single(t => t.TextContent.Contains("Discussion")).Click());
        // The feedback box starts empty (bound to the state, not showing its name).
        Assert.All(cut.FindComponents<MudTextField<string>>().Where(f => f.Instance.Label is "What should Copilot change?"),
            f => Assert.True(string.IsNullOrEmpty(f.Instance.GetState(x => x.Value))));
        await cut.InvokeAsync(() => cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "What should Copilot change?").Find("textarea").Input("Almost done."));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Send to Copilot")).Click());

        cut.WaitForAssertion(() => Assert.Contains("View it on GitHub", cut.Markup));
        using var review = System.Text.Json.JsonDocument.Parse(_gitHub.Bodies["POST /repos/owner/repo/pulls/12/reviews"]!);
        Assert.StartsWith("@copilot Almost done.", review.RootElement.GetProperty("body").GetString());
        var comment = Assert.Single(review.RootElement.GetProperty("comments").EnumerateArray());
        Assert.Equal(("src/App.cs", 1, "LEFT", "Use a clearer name"),
            (comment.GetProperty("path").GetString(), comment.GetProperty("line").GetInt32(), comment.GetProperty("side").GetString(), comment.GetProperty("body").GetString()));
        Assert.DoesNotContain("1 pending", cut.Markup);
    }

    [Fact]
    public async Task PullRequestBranch_IsALinkAtTheTop_AndCanBeCopied()
    {
        await SelectRepositoryAsync();
        var task = await SaveAcceptedTaskAsync();
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.PullRequestNumber = 12;
            entity.PullRequestUrl = "https://github.com/owner/repo/pull/12";
            entity.PullRequestBranch = "copilot/fix-#7";
            await db.SaveChangesAsync(_cancellationToken);
        }
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        cut.WaitForAssertion(() => Assert.Equal("copilot/fix-#7", cut.Find(".pr-branch a").TextContent.Trim()));
        Assert.Equal("https://github.com/owner/repo/tree/copilot/fix-%237", cut.Find(".pr-branch a").GetAttribute("href"));
        await cut.InvokeAsync(() => cut.Find("button[aria-label='Copy branch name']").Click());

        var copy = Assert.Single(JSInterop.Invocations, i => i.Identifier == "navigator.clipboard.writeText");
        Assert.Equal("copilot/fix-#7", copy.Arguments.Single());
    }

    [Fact]
    public async Task WithoutAPullRequest_NoBranchIsShown()
    {
        await SelectRepositoryAsync();
        var task = await SaveAcceptedTaskAsync();
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        cut.WaitForAssertion(() => Assert.Contains("Accepted task", cut.Markup));
        Assert.Empty(cut.FindAll(".pr-branch"));
    }

    [Fact]
    public async Task PullRequest_WhileCopilotCodeReviewIsRequested_SaysToWaitForIt()
    {
        await SelectRepositoryAsync();
        var task = await SaveAcceptedTaskAsync();
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.Progress = CopilotProgress.CopilotReviewing;
            entity.PullRequestNumber = 12;
            entity.PullRequestUrl = "https://github.com/owner/repo/pull/12";
            await db.SaveChangesAsync(_cancellationToken);
        }
        _gitHub.Responses["GET /repos/owner/repo/pulls/12"] = """
            {"number":12,"title":"Fix the task","html_url":"https://github.com/owner/repo/pull/12","state":"open","draft":false,
             "user":{"login":"Copilot"},"merged_at":null,"additions":1,"deletions":1,"changed_files":0,"mergeable_state":"clean",
             "requested_reviewers":[{"login":"Copilot","type":"Bot"}]}
            """;
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/files"] = "[]";
        _gitHub.Responses["GET /repos/owner/repo/issues/12/comments"] = "[]";
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/comments"] = "[]";
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/reviews"] = "[]";
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));

        // The work item's status says Copilot is reviewing, and the pull request still counts as one that can be merged.
        cut.WaitForAssertion(() => Assert.Contains("Copilot reviewing", cut.Markup));
        await cut.InvokeAsync(() => cut.Find(".pr-mergeable-link").Click());

        // The discussion tab says to wait for its review, instead of "ready to merge".
        cut.WaitForAssertion(() => Assert.Equal("Copilot reviewing", cut.Find(".pr-copilot-reviewing").TextContent.Trim()));
        Assert.Contains("hasn't submitted its review yet", cut.Find(".pr-merge-readiness").TextContent);
        Assert.Contains("still working on it", cut.Find(".pr-copilot-review-pending").TextContent);
        Assert.DoesNotContain("Nobody reviewed this pull request yet", cut.Markup);
    }

    [Fact]
    public async Task PullRequest_ShowsTheReviewStatus_AndMergesAndClosesIt()
    {
        await SelectRepositoryAsync();
        var task = await SaveAcceptedTaskAsync();
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var entity = await db.WorkItems.SingleAsync(_cancellationToken);
            entity.Status = WorkItemStatus.InProgress;
            entity.Progress = CopilotProgress.ReadyForReview;
            entity.PullRequestNumber = 12;
            entity.PullRequestUrl = "https://github.com/owner/repo/pull/12";
            await db.SaveChangesAsync(_cancellationToken);
        }
        _gitHub.Responses["GET /repos/owner/repo/pulls/12"] = """
            {"number":12,"title":"Fix the task","html_url":"https://github.com/owner/repo/pull/12","state":"open","draft":false,
             "user":{"login":"Copilot"},"merged_at":null,"additions":1,"deletions":1,"changed_files":0,"mergeable_state":"blocked"}
            """;
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/files"] = "[]";
        _gitHub.Responses["GET /repos/owner/repo/issues/12/comments"] = """
            [{"id":1,"user":{"login":"Copilot"},"body":"> @copilot add tests\n\nAdded **tests**.<!-- session: 42 --> <script>alert(1)</script>",
              "created_at":"2026-11-12T07:00:00Z","html_url":"https://github.com/owner/repo/pull/12#issuecomment-1"}]
            """;
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/comments"] = "[]";
        _gitHub.Responses["GET /repos/owner/repo/pulls/12/reviews"] = """
            [{"id":1,"user":{"login":"copilot-pull-request-reviewer[bot]","type":"Bot"},"state":"COMMENTED","submitted_at":"2026-11-12T08:00:00Z",
              "html_url":"https://github.com/owner/repo/pull/12#pullrequestreview-1",
              "body":"<!-- ccr-overview-v2 -->\n\n**No open issues**\n\n<details>\n<summary><img src=\"https://github.githubassets.com/low.png\" alt=\"Low severity\"> Name the constant</summary>\n\nIt's a magic number.\n</details>"},
             {"id":2,"user":{"login":"djohnnie","type":"User"},"state":"COMMENTED","submitted_at":"2026-11-12T08:30:00Z","body":""}]
            """;
        _gitHub.Responses["PUT /repos/owner/repo/pulls/12/merge"] = """{"sha":"abc123","merged":true,"message":"Pull Request successfully merged"}""";
        Navigation.NavigateTo($"/tasks/{task.Id}");
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, task.Id));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".mud-tab"), t => t.TextContent.Contains("Discussion")));

        // The top of the page says the pull request can be merged and closed, and links to the discussion tab where that's done.
        Assert.Contains("can be merged and closed", cut.Find(".pr-mergeable").TextContent);
        await cut.InvokeAsync(() => cut.Find(".pr-mergeable-link").Click());
        cut.WaitForAssertion(() => Assert.Equal("Not approved yet", cut.Find(".pr-review-decision").TextContent.Trim()));
        Assert.Equal("Merge blocked", cut.Find(".pr-merge-state").TextContent.Trim());
        Assert.Contains("A branch rule blocks merging", cut.Find(".pr-merge-readiness").TextContent);
        Assert.Empty(cut.FindAll(".pr-copilot-reviewing"));
        Assert.Contains("commented", cut.Find(".pr-reviewers").TextContent);
        // Comments are Markdown: a quote of an earlier comment is a blockquote, HTML comments are hidden like on GitHub, unsafe HTML is removed.
        var comment = cut.Find(".pr-comment .pr-markdown");
        Assert.Equal("@copilot add tests", comment.QuerySelector("blockquote")!.TextContent.Trim());
        Assert.Equal("tests", comment.QuerySelector("strong")!.TextContent);
        Assert.DoesNotContain("session: 42", comment.TextContent);
        Assert.Null(comment.QuerySelector("script"));
        Assert.DoesNotContain("alert(1)", comment.TextContent);
        // A review's text is in the conversation after the comment, with GitHub's collapsible sections; a review without text isn't.
        var review = Assert.Single(cut.FindAll(".pr-review-entry"));
        Assert.Contains("reviewed", review.QuerySelector(".pr-review-verdict")!.TextContent);
        Assert.Equal("No open issues", review.QuerySelector(".pr-markdown strong")!.TextContent);
        Assert.Contains("Name the constant", review.QuerySelector("details > summary")!.TextContent);
        Assert.Equal("Low severity", review.QuerySelector("summary .md-image-alt.md-severity-low")!.TextContent);
        Assert.Null(review.QuerySelector("img"));
        Assert.DoesNotContain("ccr-overview", review.TextContent);

        // Merging (after confirming) closes the pull request; the banner and the merge card go away.
        Assert.DoesNotContain(cut.FindAll(".pr-approve"), _ => true);
        _gitHub.Effects["PUT /repos/owner/repo/pulls/12/merge"] = () => _gitHub.Responses["GET /repos/owner/repo/pulls/12"] = _gitHub.Responses["GET /repos/owner/repo/pulls/12"]
            .Replace("\"state\":\"open\"", "\"state\":\"closed\"").Replace("\"merged_at\":null", "\"merged_at\":\"2026-11-12T10:00:00Z\"");
        await cut.InvokeAsync(() => { _ = cut.Find("button.pr-merge").ClickAsync(new()); });
        dialogs.WaitForAssertion(() => Assert.Contains("Merge and close pull request #12", dialogs.Markup), TimeSpan.FromSeconds(5));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Merge and close").Click());

        cut.WaitForAssertion(() => Assert.Contains("Merged", cut.Markup), TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".pr-merge-card")));
        Assert.Empty(cut.FindAll(".pr-mergeable"));
        using var merge = System.Text.Json.JsonDocument.Parse(_gitHub.Bodies["PUT /repos/owner/repo/pulls/12/merge"]!);
        Assert.Equal("merge", merge.RootElement.GetProperty("merge_method").GetString());
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
        Assert.Equal(["Passkeys", "Not part of another work item"], cut.FindAll("tbody th").Select(th => th.TextContent.Trim()));
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == $"/epics/{epic.Id}");
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/bugs/new");
        AssertTemplatesLinkFollowsNew(cut, "/bugs/new", "/bug-templates");
        Assert.Equal(["Bug", "Status", "Issue", "Copilot", "Agent", "Model", "Updated", "Actions"], cut.FindAll("thead th").Select(th => th.TextContent.Trim()));

        Navigation.NavigateTo("/epics");
        var epics = Render<WorkItems>();
        epics.WaitForAssertion(() => Assert.Contains("Passkeys", epics.Markup));
        Assert.DoesNotContain("Child bug", epics.Markup);
        Assert.Equal(["Epic", "Status", "Issue", "Updated", "Actions"], epics.FindAll("th").Select(th => th.TextContent.Trim()));
        AssertTemplatesLinkFollowsNew(epics, "/epics/new", "/epic-templates");
    }

    // The template pages aren't in the navigation: each list page links its templates right after its New button.
    private static void AssertTemplatesLinkFollowsNew(IRenderedComponent<WorkItems> list, string newHref, string templatesHref)
    {
        var hrefs = list.FindAll("a").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(hrefs.IndexOf(newHref) + 1, hrefs.IndexOf(templatesHref));
        Assert.Equal("Templates", list.FindAll("a").Single(a => a.GetAttribute("href") == templatesHref).TextContent.Trim());
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
    public async Task Assistant_FillsTheTechnicalDetailsFromTheWholeParentHierarchy()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        var epic = await Items.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Platform", Description = "About it.", TechnicalDetails = "Use PostgreSQL."
        }, _cancellationToken);
        var feature = await Items.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Feature, ParentId = epic.Id, Title = "Accounts", Description = "About it."
        }, _cancellationToken);
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("get_parent_technical_details", new Dictionary<string, object?>()),
            ScriptedChatClient.ToolCall("update_work_item_form", new Dictionary<string, object?>
            {
                ["technicalDetails"] = "Store accounts in PostgreSQL.\nUse EF Core."
            }),
            ScriptedChatClient.Text("Added the technical details."));
        Navigation.NavigateTo(WorkItemRoutes.NewPath(WorkItemType.Story, feature.Id));
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>();

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Sign up");
        await SendToCopilotAsync(cut, "We use EF Core");
        cut.WaitForAssertion(() => Assert.Contains("Added the technical details.", cut.Markup), TimeSpan.FromSeconds(5));

        // The request names the parents with technical details (the epic, not the feature without them)...
        var request = _chatClient.Requests[0].Last(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Text;
        Assert.Contains("\"parentsWithTechnicalDetails\": [\n    \"Epic: Platform\"\n  ]", request.ReplaceLineEndings("\n"));
        // ...and the tool reads the whole hierarchy, closest parent first.
        var parents = _chatClient.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().Single().Result?.ToString();
        Assert.True(parents!.IndexOf("Accounts", StringComparison.Ordinal) < parents.IndexOf("Platform", StringComparison.Ordinal));
        Assert.Contains("Use PostgreSQL.", parents);

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Sign up");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save draft")).Click());

        var story = Assert.Single(await Items.GetAllAsync(Repo, [WorkItemType.Story], cancellationToken: _cancellationToken));
        Assert.Equal("Store accounts in PostgreSQL.\nUse EF Core.", story.TechnicalDetails);
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
    public async Task ChatSuggestions_GetTheMindmapsAndTheParentWorkItemsOfTheRepository()
    {
        await SelectRepositoryAsync();
        var epic = await Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Security", Description = "Safer." }, _cancellationToken);
        var feature = await Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Feature, ParentId = epic.Id, Title = "Passkeys", Description = "No passwords." }, _cancellationToken);
        await Items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Task, ParentId = feature.Id, Title = "Bump the WebAuthn package", Description = "Leaf." }, _cancellationToken);
        var map = Contracts.Mindmaps.Mindmap.Create(Repo, "Release 2.0");
        MindmapLayout.AddChild(map, map.Root!.Id, "Dark mode");
        await Services.GetRequiredService<Application.Features.Mindmaps.MindmapService>().SaveAsync(map, _cancellationToken);
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("""{"suggestions":["Add dark mode"]}"""));
        Navigation.NavigateTo("/tasks/new");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>();

        cut.WaitForAssertion(() => Assert.Contains("Add dark mode", cut.Markup), TimeSpan.FromSeconds(5));
        var message = string.Join("\n", Assert.Single(_chatClient.Requests).Where(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Select(m => m.Text));
        Assert.Contains($"Repository: {Repo}", message);
        Assert.Contains("- Release 2.0\n  - Dark mode", message.ReplaceLineEndings("\n"));
        Assert.Contains($"- Epic [draft] Security (id: {epic.Id})\n  - Feature [draft] Passkeys (id: {feature.Id})", message.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("Bump the WebAuthn package", message);
    }

    [Fact]
    public async Task ChatSuggestions_RefreshWhenTheParentChanges_AndGetTheSelectedParent()
    {
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Passkeys");
        var feature = await SaveAsync(WorkItemType.Feature, "Passkey login", epic.Id);
        var otherFeature = await SaveAsync(WorkItemType.Feature, "Account recovery", epic.Id);
        var story = await SaveAsync(WorkItemType.Story, "Sign in with a passkey", feature.Id);
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.Text("""{"suggestions":["First idea"]}"""),
            ScriptedChatClient.Text("""{"suggestions":["About recovery"]}"""));
        Navigation.NavigateTo($"/stories/{story.Id}");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, story.Id));
        cut.WaitForAssertion(() => Assert.Contains("First idea", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("\"parent\": \"Feature: Passkey login\"", FormOf(_chatClient.Requests[0]));

        var parent = cut.FindComponent<MudSelect<Guid?>>();
        await cut.InvokeAsync(() => parent.Instance.ValueChanged.InvokeAsync(otherFeature.Id));

        cut.WaitForAssertion(() => Assert.Contains("About recovery", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("\"parent\": \"Feature: Account recovery\"", FormOf(_chatClient.Requests[1]));

        // Choosing the same parent again doesn't ask again.
        await cut.InvokeAsync(() => parent.Instance.ValueChanged.InvokeAsync(otherFeature.Id));
        Assert.Equal(2, _chatClient.Requests.Count);

        static string FormOf(IEnumerable<Microsoft.Extensions.AI.ChatMessage> request)
        {
            return string.Join("\n", request.Where(m => m.Role == Microsoft.Extensions.AI.ChatRole.User).Select(m => m.Text));
        }
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

    [Fact]
    public async Task NewEpic_ProposesTheDefaultTemplate_OverTheFirstAlphabetically()
    {
        await SelectRepositoryAsync();
        var templates = Services.GetRequiredService<WorkItemTemplateService>();
        await templates.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Epic, Name = "A", TitleTemplate = "First", Description = "## First" }, _cancellationToken);
        var preferred = await templates.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Epic, Name = "B", TitleTemplate = "Default", Description = "## Default", IsDefault = true }, _cancellationToken);
        Navigation.NavigateTo("/epics/new");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>();

        Assert.Equal(preferred.Description, cut.FindComponent<MarkdownEditor>().Instance.Value);
    }

    [Theory]
    [InlineData(WorkItemType.Epic, "Generate features")]
    [InlineData(WorkItemType.Story, "Generate tasks")]
    public async Task GenerateButton_SavesTheChanges_AndOpensTheReviewPage(WorkItemType type, string text)
    {
        _chatClient = new ScriptedChatClient();
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        Guid? parentId = type == WorkItemType.Story ? (await SaveAsync(WorkItemType.Feature, "Feature", (await SaveAsync(WorkItemType.Epic, "Epic")).Id)).Id : null;
        var item = await SaveAsync(type, "Stored", parentId);
        Navigation.NavigateTo(WorkItemRoutes.ItemPath(type, item.Id));
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, item.Id));

        cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Title").Find("input").Input("Edited");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains(text)).Click());

        Assert.EndsWith($"{WorkItemRoutes.ItemPath(type, item.Id)}/generate", Navigation.Uri);
        Assert.True(WorkItemRoutes.IsItemPage(Navigation.Uri));
        Assert.Equal("Edited", (await Items.GetAsync(Repo, item.Id, _cancellationToken))!.Title);
    }

    [Fact]
    public async Task GenerateButton_IsHidden_WhenTheAssistantIsNotConfigured()
    {
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        Navigation.NavigateTo($"/epics/{epic.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, epic.Id));

        Assert.Contains("Add feature", cut.Markup);
        Assert.DoesNotContain("Generate features", cut.Markup);
    }

    [Fact]
    public async Task GenerateButton_IsHidden_WhenTheAgentIsDisabled()
    {
        _chatClient = new ScriptedChatClient();
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.WorkItemBreakdown);
        var epic = await SaveAsync(WorkItemType.Epic, "Epic");
        Navigation.NavigateTo($"/epics/{epic.Id}");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemEditor>(p => p.Add(e => e.Id, epic.Id));

        Assert.Contains("Add feature", cut.Markup);
        Assert.DoesNotContain("Generate features", cut.Markup);
    }

    [Fact]
    public async Task GenerateButton_IsHidden_OnNewItems_AndOnTypesThatAreNotSplit()
    {
        _chatClient = new ScriptedChatClient();
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        Navigation.NavigateTo("/epics/new");
        Render<MudPopoverProvider>();
        Assert.DoesNotContain("Generate features", Render<WorkItemEditor>().Markup);

        var bug = await SaveAsync(WorkItemType.Bug, "Bug");
        Navigation.NavigateTo($"/bugs/{bug.Id}");
        Assert.DoesNotContain(Render<WorkItemEditor>(p => p.Add(e => e.Id, bug.Id)).FindAll("button"), b => b.TextContent.Contains("Generate"));
    }

    [Fact]
    public async Task ChildGenerator_SavesOnlyTheSelectedProposals_AndNothingBefore()
    {
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("""{"items":[{"title":"Cart","description":"## Cart"},{"title":"Payment","description":"## Payment"}]}"""));
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Webshop");
        Navigation.NavigateTo(WorkItemRoutes.GeneratePath(WorkItemType.Epic, epic.Id));

        var cut = Render<WorkItemChildGenerator>(p => p.Add(e => e.Id, epic.Id));

        cut.WaitForAssertion(() => Assert.Contains("Save selected as draft (2)", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("Generate features", cut.Markup);
        Assert.Empty(await Items.GetAllAsync(Repo, [WorkItemType.Feature], cancellationToken: _cancellationToken));

        await cut.InvokeAsync(() => cut.Find("input[aria-label='Select Payment']").Change(false));
        Assert.Contains("Save selected as draft (1)", cut.Markup);
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save selected as draft")).Click());

        var saved = Assert.Single(await Items.GetAllAsync(Repo, [WorkItemType.Feature], cancellationToken: _cancellationToken));
        Assert.Equal(("Cart", "## Cart", WorkItemStatus.Draft, (Guid?)epic.Id), (saved.Title, saved.Description, saved.Status, saved.ParentId));
        Assert.EndsWith($"/epics/{epic.Id}", Navigation.Uri);
    }

    [Fact]
    public async Task ChildGenerator_ShowsProposalsInCollapsedPanels_ThatExpandTogether_AndTickingDoesNotToggleThem()
    {
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("""{"items":[{"title":"Cart","description":"A"},{"title":"Payment","description":"B"}]}"""));
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Webshop");
        Navigation.NavigateTo(WorkItemRoutes.GeneratePath(WorkItemType.Epic, epic.Id));
        var cut = Render<WorkItemChildGenerator>(p => p.Add(e => e.Id, epic.Id));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindComponents<MudExpansionPanel>().Count), TimeSpan.FromSeconds(5));
        var panels = cut.FindComponents<MudExpansionPanel>();
        Assert.All(panels, panel => Assert.False(panel.Instance.GetState(x => x.Expanded)));

        // bUnit bubbles the click up the tree: it stops at the checkbox, so it never finds the header's handler.
        await Assert.ThrowsAsync<MissingEventHandlerException>(() => cut.InvokeAsync(() => panels[1].Find("input[type='checkbox']").Click()));
        Assert.False(panels[1].Instance.GetState(x => x.Expanded));

        await cut.InvokeAsync(() => panels[0].Find(".mud-expand-panel-header").Click());
        await cut.InvokeAsync(() => panels[1].Find(".mud-expand-panel-header").Click());
        Assert.All(panels, panel => Assert.True(panel.Instance.GetState(x => x.Expanded)));
    }

    [Fact]
    public async Task ChildGenerator_SelectAll_TogglesEveryProposal_AndNeedsASelectionToSave()
    {
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("""{"items":[{"title":"Cart","description":"A"},{"title":"Payment","description":"B"}]}"""));
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Webshop");
        Navigation.NavigateTo(WorkItemRoutes.GeneratePath(WorkItemType.Epic, epic.Id));
        var cut = Render<WorkItemChildGenerator>(p => p.Add(e => e.Id, epic.Id));
        cut.WaitForAssertion(() => Assert.Contains("Save selected as draft (2)", cut.Markup), TimeSpan.FromSeconds(5));

        await cut.InvokeAsync(() => cut.FindComponents<MudCheckBox<bool>>().Single(c => c.Instance.Label == "Select all").Instance.ValueChanged.InvokeAsync(false));

        Assert.Contains("Save selected as draft (0)", cut.Markup);
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Contains("Save selected as draft")).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ChildGenerator_ShowsAFailure_AndCanGenerateAgain()
    {
        _chatClient = new ScriptedChatClient();
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Webshop");
        Navigation.NavigateTo(WorkItemRoutes.GeneratePath(WorkItemType.Epic, epic.Id));
        var cut = Render<WorkItemChildGenerator>(p => p.Add(e => e.Id, epic.Id));
        cut.WaitForAssertion(() => Assert.Contains("could not generate features", cut.Markup), TimeSpan.FromSeconds(5));

        _chatClient.Enqueue(ScriptedChatClient.Text("""{"items":[{"title":"Cart","description":"A"}]}"""));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Generate again")).Click());

        cut.WaitForAssertion(() => Assert.Contains("Save selected as draft (1)", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("could not generate features", cut.Markup);
    }

    [Fact]
    public async Task ChildGenerator_OfAnItemOfAnotherType_ShowsAnErrorAndDoesNotCallTheAgent()
    {
        _chatClient = new ScriptedChatClient();
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Webshop");
        Navigation.NavigateTo($"/features/{epic.Id}/generate");

        var cut = Render<WorkItemChildGenerator>(p => p.Add(e => e.Id, epic.Id));

        Assert.Contains("This feature does not exist", cut.Markup);
        Assert.Empty(_chatClient.Requests);
    }

    [Fact]
    public async Task Navigation_FromAnItemToTheOverviewOfAnotherType_StaysOnTheOverview()
    {
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Webshop");
        Render<MudPopoverProvider>();
        Navigation.NavigateTo($"/epics/{epic.Id}");
        var cut = RenderWithRouter();
        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<WorkItemEditor>()), TimeSpan.FromSeconds(5));
        var navigations = CountNavigations();

        await cut.InvokeAsync(() => Navigation.NavigateTo("/features"));

        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<WorkItems>()), TimeSpan.FromSeconds(5));
        await Task.Delay(200, _cancellationToken);
        Assert.EndsWith("/features", Navigation.Uri);
        Assert.Single(navigations);
    }

    [Fact]
    public async Task Navigation_FromAnEpicToItsFeature_OpensTheFeatureWithoutRedirecting()
    {
        await SelectRepositoryAsync();
        var epic = await SaveAsync(WorkItemType.Epic, "Webshop");
        var feature = await SaveAsync(WorkItemType.Feature, "Cart", epic.Id);
        Render<MudPopoverProvider>();
        Navigation.NavigateTo($"/epics/{epic.Id}");
        var cut = RenderWithRouter();
        cut.WaitForAssertion(() => Assert.Contains("Webshop", cut.Markup), TimeSpan.FromSeconds(5));
        var navigations = CountNavigations();

        await cut.InvokeAsync(() => Navigation.NavigateTo($"/features/{feature.Id}"));

        cut.WaitForAssertion(() => Assert.Contains("Cart", cut.Find("h4").TextContent), TimeSpan.FromSeconds(5));
        await Task.Delay(200, _cancellationToken);
        Assert.EndsWith($"/features/{feature.Id}", Navigation.Uri);
        Assert.Single(navigations);
    }

    /// <summary>The real router, set up like Routes.razor: the route data cascades to the page.</summary>
    private IRenderedComponent<Router> RenderWithRouter()
    {
        return Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(WorkItemEditor).Assembly)
            .Add(router => router.Found, (RouteData routeData) => (RenderFragment)(builder =>
            {
                builder.OpenComponent<CascadingValue<RouteData>>(0);
                builder.AddComponentParameter(1, "Value", routeData);
                builder.AddComponentParameter(2, "ChildContent", (RenderFragment)(content =>
                {
                    content.OpenComponent<RouteView>(0);
                    content.AddComponentParameter(1, "RouteData", routeData);
                    content.CloseComponent();
                }));
                builder.CloseComponent();
            })));
    }

    private List<string> CountNavigations()
    {
        var navigations = new List<string>();
        Navigation.LocationChanged += (_, args) => navigations.Add(args.Location);
        return navigations;
    }

    private Task<WorkItem> SaveAsync(WorkItemType type, string title, Guid? parentId = null)
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
        /// <summary>What GitHub returns for an issue; without it, like everything else, it isn't found.</summary>
        public string? IssueJson { get; set; }

        /// <summary>Canned answers by "METHOD /path"; they win over <see cref="IssueJson"/>.</summary>
        public Dictionary<string, string> Responses { get; } = [];

        /// <summary>The bodies of the requests answered from <see cref="Responses"/>, by "METHOD /path".</summary>
        public Dictionary<string, string?> Bodies { get; } = [];

        /// <summary>What changes on GitHub when a request is answered, by "METHOD /path", e.g. a merge closes the pull request.</summary>
        public Dictionary<string, Action> Effects { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = $"{request.Method.Method} {request.RequestUri!.AbsolutePath}";
            if (Responses.TryGetValue(key, out var json))
            {
                Bodies[key] = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                if (Effects.TryGetValue(key, out var effect))
                {
                    effect();
                }
                return new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }
            return await AnswerAsync(request);
        }

        private Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request)
        {
            if (IssueJson is not null && request.RequestUri!.AbsolutePath.Contains("/issues/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(IssueJson, System.Text.Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
