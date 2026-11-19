using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Application.Features.ProjectChats;
using Djohnnie.SoftwareFactory.Application.Features.Speech;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.Contracts.Speech;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using WorkItem = Djohnnie.SoftwareFactory.Contracts.WorkItems.WorkItem;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ProjectChatTests : BunitContext
{
    private const string Repo = "owner/repo";
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;
    private readonly TitleRoutingChatClient _chatClient = new();

    public ProjectChatTests()
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
        Services.AddScoped<IGitHubTokenProvider, GitHubTokenProvider>();
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(new NotFoundHandler()) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    private IProjectChatStore Store => Services.GetRequiredService<IProjectChatStore>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();
    private InternalAgentService Agents => Services.GetRequiredService<InternalAgentService>();

    [Fact]
    public void Defaults_IncludeTheDghsfAgent_AsAChatAgentWithItsOwnUsageSource()
    {
        var agent = InternalAgentDefaults.Find(InternalAgentKeys.DghsfAgent)!;

        Assert.Equal("DGHSF Agent", agent.Name);
        Assert.Equal(InternalAgentKind.Copilot, agent.Kind);
        Assert.Contains("`get_work_items`", agent.Instructions);
        Assert.Contains("`read_repository_file`", agent.Instructions);
        Assert.Equal(InternalAgentUsageSource.DghsfAgentChat, InternalAgentUsageSources.ForCopilot(InternalAgentKeys.DghsfAgent));
        Assert.Equal("Chat", InternalAgents.UsedOn(agent));
    }

    [Fact]
    public async Task Store_ListsConversationsByLastUse_PerRepository_AndKeepsTheirMessages()
    {
        var older = await Store.StartAsync(Repo, "Older chat", _cancellationToken);
        var newer = await Store.StartAsync(Repo, "Newer chat", _cancellationToken);
        await Store.StartAsync("other/repo", "Elsewhere", _cancellationToken);

        Assert.Equal(["Newer chat", "Older chat"], (await Store.GetAllAsync(Repo, _cancellationToken)).Select(c => c.Title));

        await Task.Delay(20, _cancellationToken);
        var used = await Store.AppendAsync(Repo, older.Id,
            [new StoredChatMessage(ChatMessageRole.User, "Question", "{}"), new StoredChatMessage(ChatMessageRole.Tool, null, "{\"tool\":1}"),
             new StoredChatMessage(ChatMessageRole.Assistant, "Answer", "{}")], _cancellationToken);

        Assert.True(used.LastUsedAt > older.LastUsedAt);
        Assert.Equal(older.CreatedAt, used.CreatedAt);
        Assert.Equal(["Older chat", "Newer chat"], (await Store.GetAllAsync("OWNER/REPO", _cancellationToken)).Select(c => c.Title));
        var chat = (await Store.GetAsync(Repo, older.Id, _cancellationToken))!;
        Assert.Equal(["Question", "Answer"], chat.Messages.Select(m => m.Text));
        Assert.Equal(3, (await Store.GetPayloadsAsync(Repo, older.Id, _cancellationToken)).Count);
        Assert.Null(await Store.GetAsync("other/repo", older.Id, _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => Store.AppendAsync("other/repo", older.Id, [new StoredChatMessage(ChatMessageRole.User, "x", null)], _cancellationToken));

        var renamed = await Store.RenameAsync(Repo, newer.Id, "  Release   planning  ", _cancellationToken);
        Assert.Equal("Release planning", renamed.Title);

        await Store.DeleteAsync("other/repo", older.Id, _cancellationToken);
        Assert.NotNull(await Store.GetAsync(Repo, older.Id, _cancellationToken));
        await Store.DeleteAsync(Repo, older.Id, _cancellationToken);
        Assert.Equal(["Release planning"], (await Store.GetAllAsync(Repo, _cancellationToken)).Select(c => c.Title));
    }

    [Fact]
    public async Task Store_SummarizesEveryIntervalOfChatMessages_ReplaysTheLastSummaryAndWhatFollows_ButShowsEveryMessage()
    {
        var chat = await Store.StartAsync(Repo, "Long chat", _cancellationToken);
        for (var turn = 1; turn <= 4; turn++)
        {
            await Store.AppendAsync(Repo, chat.Id, Turn(turn), _cancellationToken);
        }
        Assert.Null(await Store.GetHistoryToSummarizeAsync(Repo, chat.Id, ProjectChat.SummaryInterval, _cancellationToken));
        await Store.AppendAsync(Repo, chat.Id,
            [new StoredChatMessage(ChatMessageRole.User, "Q5", "{\"q\":5}"), new StoredChatMessage(ChatMessageRole.Assistant, "Failed", null, IsError: true)],
            _cancellationToken);

        var history = (await Store.GetHistoryToSummarizeAsync(Repo, chat.Id, ProjectChat.SummaryInterval, _cancellationToken))!;
        Assert.Null(history.PreviousSummary);
        Assert.Equal(["Q1", "A1", "Q2", "A2", "Q3", "A3", "Q4", "A4", "Q5"], history.Messages.Select(m => m.Text));
        Assert.Null(await Store.GetHistoryToSummarizeAsync("other/repo", chat.Id, ProjectChat.SummaryInterval, _cancellationToken));

        // An exchange stored while the summary is written isn't in it, so it stays in what the model gets.
        await Store.AppendAsync(Repo, chat.Id, Turn(6), _cancellationToken);
        await Store.SaveSummaryAsync(Repo, chat.Id, "First summary", "{\"summary\":1}", history.UpToSequence, _cancellationToken);

        Assert.Equal(["{\"summary\":1}", "{\"q\":6}", "{\"tool\":6}", "{\"a\":6}"], await Store.GetPayloadsAsync(Repo, chat.Id, _cancellationToken));
        var shown = (await Store.GetAsync(Repo, chat.Id, _cancellationToken))!.Messages.Select(m => m.Text).ToList();
        Assert.Equal(12, shown.Count);
        Assert.DoesNotContain("First summary", shown);

        for (var turn = 7; turn <= 10; turn++)
        {
            await Store.AppendAsync(Repo, chat.Id, Turn(turn), _cancellationToken);
        }
        var next = (await Store.GetHistoryToSummarizeAsync(Repo, chat.Id, ProjectChat.SummaryInterval, _cancellationToken))!;
        Assert.Equal("First summary", next.PreviousSummary);
        Assert.Equal("Q6", next.Messages[0].Text);
        await Store.SaveSummaryAsync(Repo, chat.Id, "Second summary", "{\"summary\":2}", next.UpToSequence, _cancellationToken);

        Assert.Equal(["{\"summary\":2}"], await Store.GetPayloadsAsync(Repo, chat.Id, _cancellationToken));
        Assert.Equal(20, (await Store.GetAsync(Repo, chat.Id, _cancellationToken))!.Messages.Count);
    }

    [Fact]
    public async Task Conversation_IsSummarizedAfterTenMessages_AndTheAgentThenGetsTheSummaryAndTheMessagesAfterIt()
    {
        // As at startup: usage is recorded on the stored agent.
        await Agents.SeedAsync(_cancellationToken);
        var service = Services.GetRequiredService<ProjectChatService>();
        var agent = await service.GetAgentAsync(_cancellationToken);
        var chat = await service.StartAsync(Repo, "Q1", _cancellationToken);
        var conversation = service.CreateConversation(agent, Repo, chat.Id, service.CreateTools(Repo));
        for (var turn = 1; turn <= 5; turn++)
        {
            Assert.False(await conversation.SummarizeIfDueAsync(_cancellationToken));
            await RunTurnAsync(conversation, turn);
        }

        Assert.True(await conversation.SummarizeIfDueAsync(_cancellationToken));
        var summaryRequest = Assert.Single(_chatClient.SummaryRequests);
        Assert.Contains("(none: this is the first summary)", summaryRequest);
        Assert.Contains("### User\nQ1", summaryRequest);
        Assert.Contains("### DGHSF Agent\nA5", summaryRequest);
        Assert.False(await conversation.SummarizeIfDueAsync(_cancellationToken));
        var usage = await WaitForAsync(async () => (await Agents.GetUsageAsync(InternalAgentKeys.ChatSummarizer, _cancellationToken)).SingleOrDefault());
        Assert.Equal((InternalAgentUsageSource.ChatSummarizer, 300L, 30L), (usage.Source, usage.InputTokens, usage.OutputTokens));

        await RunTurnAsync(conversation, 6);
        var sent = _chatClient.AgentRequests[^1];
        Assert.Contains(sent, m => m.Role == ChatRole.System && m.Text.Contains("Summary 1: the user asked about the backlog.", StringComparison.Ordinal));
        Assert.DoesNotContain(sent, m => m.Text is "Q1" or "A5");
        Assert.Equal("Q6", sent.Last(m => m.Role == ChatRole.User).Text);

        // A reopened conversation starts from the summary as well; the chat still shows every message.
        var reopened = service.CreateConversation(agent, Repo, chat.Id, service.CreateTools(Repo));
        await RunTurnAsync(reopened, 7);
        Assert.Equal(["Summary", "Q6", "A6", "Q7"], _chatClient.AgentRequests[^1]
            .Where(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant || m.Text.Contains("was summarized", StringComparison.Ordinal))
            .Select(m => m.Role == ChatRole.System ? "Summary" : m.Text));
        Assert.Equal(14, (await service.GetAsync(Repo, chat.Id, _cancellationToken))!.Messages.Count);
    }

    [Fact]
    public async Task DisabledSummarizer_KeepsSendingEveryMessage()
    {
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSummarizer);
        var service = Services.GetRequiredService<ProjectChatService>();
        var agent = await service.GetAgentAsync(_cancellationToken);
        var chat = await service.StartAsync(Repo, "Q1", _cancellationToken);
        var conversation = service.CreateConversation(agent, Repo, chat.Id, service.CreateTools(Repo));
        for (var turn = 1; turn <= 5; turn++)
        {
            await RunTurnAsync(conversation, turn);
        }

        Assert.False(await conversation.SummarizeIfDueAsync(_cancellationToken));
        Assert.Empty(_chatClient.SummaryRequests);
        Assert.Equal(10, (await Store.GetPayloadsAsync(Repo, chat.Id, _cancellationToken)).Count);
    }

    private async Task RunTurnAsync(ProjectConversation conversation, int turn)
    {
        _chatClient.Replies.Enqueue($"A{turn}");
        var reply = string.Empty;
        await foreach (var update in conversation.RunStreamingAsync($"Q{turn}", _cancellationToken))
        {
            reply += update;
        }
        await conversation.SaveTurnAsync($"Q{turn}", reply, false, _cancellationToken);
    }

    private static List<StoredChatMessage> Turn(int turn)
    {
        return
        [
            new StoredChatMessage(ChatMessageRole.User, $"Q{turn}", $"{{\"q\":{turn}}}"),
            new StoredChatMessage(ChatMessageRole.Tool, null, $"{{\"tool\":{turn}}}"),
            new StoredChatMessage(ChatMessageRole.Assistant, $"A{turn}", $"{{\"a\":{turn}}}")
        ];
    }

    [Theory]
    [InlineData("Backlog status", "Backlog status")]
    [InlineData("\"login bugs.\"", "Login bugs")]
    [InlineData("Release planning for Q3", "Release planning")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void CleanChatTitle_KeepsTwoWordsWithoutPunctuation(string? title, string? expected)
    {
        Assert.Equal(expected, BackgroundAgents.CleanChatTitle(title));
    }

    [Fact]
    public void FallbackTitle_IsTheStartOfTheFirstMessage()
    {
        Assert.Equal("What is open?", ProjectChatService.FallbackTitle("  What   is\nopen? "));
        Assert.Equal(ProjectChat.DefaultTitle, ProjectChatService.FallbackTitle(" "));
        var title = ProjectChatService.FallbackTitle("Which work items are blocked by failing checks on their pull requests right now?");
        Assert.True(title.Length <= ProjectChatService.FallbackTitleLength + 1);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public async Task Tools_ReadTheBacklog_FilterAndSearchIt_AndOnlyReadItemsOfTheRepository()
    {
        var epic = new WorkItem { Id = Guid.NewGuid(), RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Checkout", Description = "Payments", Status = WorkItemStatus.Accepted, IssueNumber = 3, IssueUrl = "https://github.com/owner/repo/issues/3" };
        var story = new WorkItem
        {
            Id = Guid.NewGuid(), RepositoryFullName = Repo, Type = WorkItemType.Story, Title = "Pay by card", Description = "Use the card provider.",
            TechnicalDetails = "Stripe webhook", ParentId = epic.Id, Parent = new WorkItemReference(epic.Id, WorkItemType.Epic, epic.Title, epic.Status, epic.IssueNumber, epic.IssueUrl)
        };
        var bug = new WorkItem { Id = Guid.NewGuid(), RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Login times out", Status = WorkItemStatus.Done };
        var tools = new ProjectAgentTools(new FakeProjectData([epic, story, bug]));

        var all = await tools.GetWorkItemsAsync(cancellationToken: _cancellationToken);
        Assert.Contains("- Epic [accepted, #3] Checkout", all);
        Assert.Contains("  - User story [draft] Pay by card", all);
        Assert.Contains("Bugs: 1 (1 done)", all);

        var bugs = await tools.GetWorkItemsAsync("bugs", cancellationToken: _cancellationToken);
        Assert.Contains("Login times out", bugs);
        Assert.DoesNotContain("Pay by card", bugs.Split("Matching items")[1]);
        Assert.Contains("is not a work item type", await tools.GetWorkItemsAsync("idea", cancellationToken: _cancellationToken));
        Assert.Contains("Pay by card", await tools.GetWorkItemsAsync(status: "draft", cancellationToken: _cancellationToken));

        var search = await tools.SearchWorkItemsAsync("stripe", _cancellationToken);
        Assert.Contains("Pay by card", search);
        Assert.DoesNotContain("Checkout", search);

        var details = await tools.GetWorkItemAsync(story.Id.ToString(), _cancellationToken);
        Assert.Contains("Parent: Epic Checkout", details);
        Assert.Contains("Use the card provider.", details);
        Assert.Contains("Technical details:\nStripe webhook", details.ReplaceLineEndings("\n"));
        Assert.Contains("Children:", await tools.GetWorkItemAsync(epic.Id.ToString(), _cancellationToken));
        Assert.Contains("no work item with id", await tools.GetWorkItemAsync(Guid.NewGuid().ToString(), _cancellationToken));
    }

    [Fact]
    public async Task Tools_GiveThePageOfEveryWorkItem_ThatTheChatRendersAsALinkWithinTheApp()
    {
        var epic = new WorkItem { Id = Guid.NewGuid(), RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Checkout" };
        var story = new WorkItem
        {
            Id = Guid.NewGuid(), RepositoryFullName = Repo, Type = WorkItemType.Story, Title = "Pay by card", ParentId = epic.Id,
            Parent = new WorkItemReference(epic.Id, WorkItemType.Epic, epic.Title, epic.Status, epic.IssueNumber, epic.IssueUrl)
        };
        var tools = new ProjectAgentTools(new FakeProjectData([epic, story]));

        Assert.Contains($"page: /stories/{story.Id}", await tools.GetWorkItemsAsync(cancellationToken: _cancellationToken));
        Assert.Contains($"page: /stories/{story.Id}", await tools.SearchWorkItemsAsync("card", _cancellationToken));
        var details = await tools.GetWorkItemAsync(story.Id.ToString(), _cancellationToken);
        Assert.Contains($"page: /stories/{story.Id}", details);
        Assert.Contains($"page: /epics/{epic.Id}", details);
        Assert.Equal(WorkItemRoutes.ItemPath(WorkItemType.Story, story.Id), $"/stories/{story.Id}");

        var html = ChatMarkdown.ToHtml($"See [Pay by card](/stories/{story.Id}).", allowAppLinks: true);
        Assert.Contains($"href=\"/stories/{story.Id}\"", html);
        Assert.DoesNotContain("target=", html);
    }

    [Fact]
    public async Task Reply_LinksWorkItemsWithinDghsf_EvenWhenTheAgentLinksTheirGitHubIssue()
    {
        await Agents.SeedAsync(_cancellationToken);
        await SelectRepositoryAsync();
        var store = Services.GetRequiredService<IWorkItemStore>();
        var bug = await store.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Login times out" }, _cancellationToken);
        await store.MarkAcceptedAsync(Repo, bug.Id, 7, 7, "https://github.com/owner/repo/issues/7", _cancellationToken);
        _chatClient.Replies.Enqueue($"See [Login times out](https://github.com/owner/repo/issues/7) and [the same](/bugs/{bug.Id}).");
        Navigation.NavigateTo("/chat");
        Render<MudPopoverProvider>();
        var cut = Render<Chat>();

        await SendAsync(cut, "Which bugs are open?");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll($".chat-markdown a[href='/bugs/{bug.Id}']").Count), TimeSpan.FromSeconds(5));
        Assert.Empty(cut.FindAll(".chat-markdown a[href^='https://github.com']"));
        Assert.Empty(cut.FindAll(".chat-markdown a[target]"));
        Assert.Contains(ProjectAgentTools.LinkInstructions.Trim().ReplaceLineEndings("\n"), _chatClient.AgentInstructions[^1].ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task FirstMessage_StartsAStoredConversation_NamedByTheTitleWriter_AndReopeningItContinuesIt()
    {
        await Agents.SeedAsync(_cancellationToken);
        await SelectRepositoryAsync();
        _chatClient.Replies.Enqueue("The backlog has **one** epic.");
        Navigation.NavigateTo("/chat");
        Render<MudPopoverProvider>();
        var cut = Render<Chat>();

        await SendAsync(cut, "What is in the backlog?");

        cut.WaitForAssertion(() => Assert.Contains("<strong>one</strong>", cut.Markup), TimeSpan.FromSeconds(5));
        var chat = await WaitForAsync(async () => (await Store.GetAllAsync(Repo, _cancellationToken)).SingleOrDefault(c => c.Title == "Backlog overview"));
        Assert.EndsWith($"/chat/{chat.Id}", Navigation.Uri);
        cut.WaitForAssertion(() => Assert.Contains("Backlog overview", cut.Find(".project-chat-list").TextContent), TimeSpan.FromSeconds(5));
        await WaitForAsync(async () => (await Store.GetAsync(Repo, chat.Id, _cancellationToken))!.Messages.Count == 2 ? chat : null);
        Assert.Contains(_chatClient.TitleRequests, text => text.Contains("What is in the backlog?", StringComparison.Ordinal));
        var titleUsage = await WaitForAsync(async () => (await Agents.GetUsageAsync(InternalAgentKeys.ChatTitleWriter, _cancellationToken)).SingleOrDefault());
        Assert.Equal((InternalAgentUsageSource.ChatTitleWriter, 40L, 5L), (titleUsage.Source, titleUsage.InputTokens, titleUsage.OutputTokens));

        _chatClient.Replies.Enqueue("Still one.");
        var reopened = Render<Chat>(p => p.Add(c => c.Id, chat.Id));
        reopened.WaitForAssertion(() => Assert.Contains("What is in the backlog?", reopened.Find(".project-chat-conversation").TextContent), TimeSpan.FromSeconds(5));
        await SendAsync(reopened, "And now?");
        reopened.WaitForAssertion(() => Assert.Contains("Still one.", reopened.Markup), TimeSpan.FromSeconds(5));

        var replayed = _chatClient.AgentRequests[^1];
        Assert.Contains(replayed, m => m.Role == ChatRole.User && m.Text == "What is in the backlog?");
        Assert.Contains(replayed, m => m.Role == ChatRole.Assistant && m.Text == "The backlog has **one** epic.");
        await WaitForAsync(async () => (await Store.GetAsync(Repo, chat.Id, _cancellationToken))!.Messages.Count == 4 ? chat : null);
        Assert.Single(await Store.GetAllAsync(Repo, _cancellationToken));
    }

    [Fact]
    public async Task DisabledAgent_CantBeSentMessages()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.DghsfAgent);
        Navigation.NavigateTo("/chat");
        Render<MudPopoverProvider>();
        var cut = Render<Chat>();

        cut.WaitForAssertion(() => Assert.Contains("The DGHSF Agent is disabled", cut.Markup), TimeSpan.FromSeconds(5));
        await SendAsync(cut, "Hello?", whenReady: false);
        Assert.Empty(await Store.GetAllAsync(Repo, _cancellationToken));
    }

    [Fact]
    public async Task DisabledTitleWriter_KeepsTheStartOfTheFirstMessageAsTitle()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatTitleWriter);
        var chat = await Services.GetRequiredService<ProjectChatService>().StartAsync(Repo, "What is in the backlog?", _cancellationToken);

        var named = await Services.GetRequiredService<ProjectChatService>().WriteTitleAsync(Repo, chat.Id, "What is in the backlog?", _cancellationToken);

        Assert.Null(named);
        Assert.Empty(_chatClient.TitleRequests);
        Assert.Equal("What is in the backlog?", (await Store.GetAsync(Repo, chat.Id, _cancellationToken))!.Title);
    }

    [Fact]
    public async Task Microphone_FillsTheInputWithWhatIsSaid_ToSendWithEnter()
    {
        Services.AddSingleton<ISpeechTokenIssuer>(new FakeSpeechTokenIssuer());
        Services.Configure<SpeechOptions>(options =>
        {
            options.Region = "westeurope";
            options.ApiKey = "speech-key";
        });
        await SelectRepositoryAsync();
        var dictation = JSInterop.SetupModule("./js/dictation.js");
        var recognition = dictation.Setup<DictationTextField.DictationResult>("recognizeOnce", _ => true);
        _chatClient.Replies.Enqueue("Two bugs are open.");
        Navigation.NavigateTo("/chat");
        Render<MudPopoverProvider>();
        var cut = Render<Chat>();

        var field = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Ask DGHSF Agent about this project");
        cut.WaitForAssertion(() => Assert.Equal("Dictate a message", field.Instance.AdornmentAriaLabel), TimeSpan.FromSeconds(5));
        var listening = cut.InvokeAsync(() => field.Find("button[aria-label='Dictate a message']").Click());
        cut.WaitForAssertion(() => Assert.Equal("Stop dictating", field.Instance.AdornmentAriaLabel));
        Assert.Contains("dictation-listening", field.Instance.Class);
        Assert.Equal("token-1", Assert.Single(dictation.Invocations["recognizeOnce"]).Arguments[0]);

        recognition.SetResult(new("recognized", " Which bugs are open? "));
        await listening;

        cut.WaitForAssertion(() => Assert.Equal("Which bugs are open?", field.Find("input").GetAttribute("value")));
        Assert.Equal("Send (Enter)", field.Instance.AdornmentAriaLabel);
        await cut.InvokeAsync(() => field.Find("input").KeyDown("Enter"));
        cut.WaitForAssertion(() => Assert.Contains("Two bugs are open.", cut.Markup), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Types and sends a message. Unless <paramref name="whenReady"/> is false, it first waits until the page has loaded the
    /// agent: the input is disabled, and Enter does nothing, until then.
    /// </summary>
    private static async Task SendAsync(IRenderedComponent<Chat> cut, string text, bool whenReady = true)
    {
        var field = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Ask DGHSF Agent about this project");
        if (whenReady)
        {
            cut.WaitForAssertion(() => Assert.False(field.Find("input").HasAttribute("disabled")), TimeSpan.FromSeconds(5));
        }
        var input = field.Find("input");
        input.Input(text);
        await cut.InvokeAsync(() => input.KeyDown("Enter"));
    }

    private async Task<T> WaitForAsync<T>(Func<Task<T?>> find) where T : class
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await find() is { } found)
            {
                return found;
            }
            await Task.Delay(50, _cancellationToken);
        }

        Assert.Fail("The expected state was not reached.");
        return null!;
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", Repo, new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));
    }

    private sealed class FakeSpeechTokenIssuer : ISpeechTokenIssuer
    {
        public Task<IssuedSpeechToken> IssueAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new IssuedSpeechToken("token-1", DateTimeOffset.UtcNow.AddMinutes(10)));
        }
    }

    /// <summary>Answers the Chat Title Writer with a fixed title and the DGHSF Agent with the queued replies, in any order.</summary>
    private sealed class TitleRoutingChatClient : IChatClient
    {
        public Queue<string> Replies { get; } = new();
        public List<List<ChatMessage>> AgentRequests { get; } = [];
        public List<string> AgentInstructions { get; } = [];
        public List<string> TitleRequests { get; } = [];
        public List<string> SummaryRequests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            lock (this)
            {
                var instructions = options?.Instructions + string.Join("\n", list.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
                if (instructions.Contains("You name conversations", StringComparison.Ordinal))
                {
                    TitleRequests.Add(string.Join("\n", list.Select(m => m.Text)));
                    return Task.FromResult(ScriptedChatClient.WithUsage(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"title\":\"Backlog overview\"}")), 40, 5));
                }

                if (instructions.Contains("You are the Chat Summarizer", StringComparison.Ordinal))
                {
                    var request = string.Join("\n", list.Select(m => m.Text));
                    SummaryRequests.Add(request);
                    var summary = System.Text.Json.JsonSerializer.Serialize(new { summary = $"Summary {SummaryRequests.Count}: the user asked about the backlog." });
                    return Task.FromResult(ScriptedChatClient.WithUsage(new ChatResponse(new ChatMessage(ChatRole.Assistant, summary)), 300, 30));
                }

                AgentRequests.Add(list);
                AgentInstructions.Add(instructions);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Replies.Dequeue())));
            }
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeProjectData(IReadOnlyList<WorkItem> items) : IProjectAgentData
    {
        public string RepositoryFullName => Repo;
        public bool CanSearchKnowledgeBase => false;

        public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(items);
        }

        public Task<WorkItem?> GetWorkItemAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(items.FirstOrDefault(i => i.Id == id));
        }

        public Task<IReadOnlyList<MindmapSummary>> GetMindmapsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<MindmapSummary>>([]);
        }

        public Task<Mindmap?> GetMindmapAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Mindmap?>(null);
        }

        public Task<KnowledgeLibrary> GetKnowledgeLibraryAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(KnowledgeLibrary.Empty);
        }

        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchKnowledgeBaseAsync(string query, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeSearchResult>>([]);
        }

        public Task<IReadOnlyList<WorkItemTemplate>> GetWorkItemTemplatesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WorkItemTemplate>>([]);
        }

        public Task<RepositorySkills> GetSkillsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new RepositorySkills([], []));
        }

        public Task<IReadOnlyList<TodoTaskRun>> GetBackgroundTaskRunsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<TodoTaskRun>>([]);
        }

        public Task<IReadOnlyList<SyncLogEvent>> GetSyncLogAsync(int count, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SyncLogEvent>>([]);
        }

        public Task<IReadOnlyList<RepositoryEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RepositoryEntry>>([]);
        }

        public Task<string?> ReadFileAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
