using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class InternalAgentTests : BunitContext
{
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;
    private ScriptedChatClient? _chatClient;

    public InternalAgentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLogging();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton(TimeProvider.System);
        Services.AddApplication();
        Services.AddSingleton(_ => new AssistantAgentFactory(_chatClient, NullLoggerFactory.Instance));
    }

    private InternalAgentService Agents => Services.GetRequiredService<InternalAgentService>();

    /// <summary>Disables an internal agent, e.g. Chat Suggestions in tests that script the chat responses.</summary>
    internal static async Task DisableAsync(IServiceProvider services, string key)
    {
        var agents = services.GetRequiredService<InternalAgentService>();
        await agents.SaveAsync((await agents.GetAsync(key)) with { IsEnabled = false });
    }

    [Fact]
    public void Defaults_HaveACopilotPerWorkItemTypeTemplateAgentAndInstructionsPage_AndThreeBackgroundAgents()
    {
        var keys = InternalAgentDefaults.All.Select(a => a.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(16, keys.Count);
        foreach (var type in WorkItemTypeRules.All)
        {
            Assert.Contains("`update_work_item_form`", InternalAgentDefaults.Find(InternalAgentKeys.ForWorkItem(type))!.Instructions);
            Assert.Contains("`update_template_form`", InternalAgentDefaults.Find(InternalAgentKeys.ForWorkItemTemplate(type))!.Instructions);
        }
        Assert.Contains("`update_agent_form`", InternalAgentDefaults.Find(InternalAgentKeys.AgentCopilot)!.Instructions);
        Assert.Contains("This is an agent template", InternalAgentDefaults.Find(InternalAgentKeys.AgentTemplateCopilot)!.Instructions);
        Assert.Equal([InternalAgentKeys.ModelSelector, InternalAgentKeys.ChatSuggestions, InternalAgentKeys.DescriptionWriter],
            InternalAgentDefaults.All.Where(a => a.Kind == InternalAgentKind.Background).Select(a => a.Key));
        Assert.Equal("User Story Copilot", InternalAgentDefaults.Find("story-copilot")!.Name);
        Assert.StartsWith("You are User Story Copilot,", InternalAgentDefaults.Find("story-copilot")!.Instructions);
        Assert.All(InternalAgentDefaults.All, a => Assert.True(a.IsEnabled && !a.IsCustomized && a.Instructions.Length < InternalAgent.MaxInstructionsLength));
    }

    [Fact]
    public async Task Seed_AddsMissingAgentsOnce_AndKeepsCustomizedPrompts()
    {
        Assert.Equal(InternalAgentDefaults.All.Count, (await Agents.GetAllAsync(_cancellationToken)).Count);
        var story = await Agents.GetAsync(InternalAgentKeys.ForWorkItem(WorkItemType.Story), _cancellationToken);
        var saved = await Agents.SaveAsync(story with { Instructions = "Only write tiny stories.\r\n", IsEnabled = false, Name = "Renamed" }, _cancellationToken);

        await Agents.SeedAsync(_cancellationToken);
        await Agents.SeedAsync(_cancellationToken);

        var all = await Agents.GetAllAsync(_cancellationToken);
        Assert.Equal(InternalAgentDefaults.All.Select(a => a.Key), all.Select(a => a.Key));
        var reloaded = all.Single(a => a.Key == story.Key);
        Assert.Equal(("Only write tiny stories.", false, "User Story Copilot"), (reloaded.Instructions, reloaded.IsEnabled, reloaded.Name));
        Assert.True(reloaded.IsCustomized);
        Assert.Equal(saved.UpdatedAt, reloaded.UpdatedAt);
        Assert.Equal(story.DefaultInstructions, reloaded.DefaultInstructions);
        Assert.All(all.Where(a => a.Key != story.Key), a => Assert.False(a.IsCustomized));
    }

    [Fact]
    public async Task Reset_RestoresTheDefaultPrompt_AndKeepsTheEnabledState()
    {
        var agent = await Agents.GetAsync(InternalAgentKeys.ModelSelector, _cancellationToken);
        await Agents.SaveAsync(agent with { Instructions = "Always pick the cheapest model.", IsEnabled = false }, _cancellationToken);

        var reset = await Agents.ResetAsync(InternalAgentKeys.ModelSelector, _cancellationToken);

        Assert.Equal(agent.DefaultInstructions, reset.Instructions);
        Assert.False(reset.IsCustomized);
        Assert.False(reset.IsEnabled);
    }

    [Fact]
    public async Task Save_RejectsAnEmptyOrTooLongPromptAndUnknownAgents()
    {
        var agent = await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken);

        await Assert.ThrowsAsync<ValidationFailedException>(() => Agents.SaveAsync(agent with { Instructions = " " }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Agents.SaveAsync(
            agent with { Instructions = new string('a', InternalAgent.MaxInstructionsLength + 1) }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Agents.SaveAsync(agent with { Key = "unknown" }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Agents.GetAsync("unknown", _cancellationToken));
        Assert.False((await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken)).IsCustomized);
    }

    [Fact]
    public async Task TemplateCopilot_UsesTheStoredPrompt_AndPrefillsTheTemplateForm()
    {
        await DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        var copilot = await Agents.GetAsync(InternalAgentKeys.ForWorkItemTemplate(WorkItemType.Bug), _cancellationToken);
        await Agents.SaveAsync(copilot with { Instructions = "You write bug templates for a webshop." }, _cancellationToken);
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("update_template_form", new Dictionary<string, object?>
            {
                ["name"] = "Crash report",
                ["titleExample"] = "[Page] crashes when [action]",
                ["descriptionSkeleton"] = "## Steps to reproduce\r\n<!-- numbered -->\n"
            }),
            ScriptedChatClient.Text("Filled in the template."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/bug-templates/new");
        Render<MudPopoverProvider>();
        var cut = Render<WorkItemTemplateEditor>();

        cut.WaitForAssertion(() => Assert.Contains("Bug Template Copilot", cut.Markup));
        Assert.Contains("A crash report with steps to reproduce", cut.Markup);
        var chatInput = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Describe the bug template you need").Find("input");
        chatInput.Input("Crashes");
        await cut.InvokeAsync(() => chatInput.KeyDown("Enter"));

        cut.WaitForAssertion(() => Assert.Contains("Filled in the template.", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("You write bug templates for a webshop.", SystemPrompt(_chatClient, 0));
        Assert.Contains("Updated: name, title example, description skeleton.",
            _chatClient.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result?.ToString());
        Assert.Equal("Crash report", cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Template name").Find("input").GetAttribute("value"));
        Assert.Equal("## Steps to reproduce\n<!-- numbered -->", cut.FindComponent<MarkdownEditor>().Instance.Value);
    }

    [Fact]
    public async Task DisabledCopilot_IsHiddenFromItsPage()
    {
        await DisableAsync(Services, InternalAgentKeys.ForWorkItemTemplate(WorkItemType.Task));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/task-templates/new");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemTemplateEditor>();

        cut.WaitForAssertion(() => Assert.Contains("Template name", cut.Markup));
        Assert.Empty(cut.FindAll(".assistant-chat"));
    }

    [Fact]
    public async Task ChatSuggestions_ReplaceTheBuiltInSuggestions_BasedOnTheCopilotAndTheForm()
    {
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text(
            """{"suggestions":["A crash report for checkout errors.","\"A template for flaky tests\"","A crash report for checkout errors","A slow page report","Too many"]}"""));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/bug-templates/new");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemTemplateEditor>();

        cut.WaitForAssertion(() => Assert.Equal(
            ["A crash report for checkout errors", "A template for flaky tests", "A slow page report"],
            cut.FindComponents<MudChip<string>>().Select(c => c.Find(".mud-chip-content").TextContent.Trim())), TimeSpan.FromSeconds(5));
        var request = Assert.Single(_chatClient.Requests);
        var message = string.Join("\n", request.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        Assert.Contains("Copilot: Bug Template Copilot", message);
        Assert.Contains("\"type\": \"Bug\"", message);
        Assert.Contains("You are Chat Suggestions", SystemPrompt(_chatClient, 0));
    }

    [Fact]
    public async Task ChatSuggestions_WhenDisabled_ShowTheBuiltInSuggestionsWithoutCallingTheModel()
    {
        _chatClient = new ScriptedChatClient();
        await DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/story-templates/new");
        Render<MudPopoverProvider>();

        var cut = Render<WorkItemTemplateEditor>();

        cut.WaitForAssertion(() => Assert.Contains("A story for an API endpoint", cut.Markup));
        Assert.Empty(_chatClient.Requests);
    }

    [Fact]
    public async Task ListPage_ShowsCopilotsAndBackgroundAgents_AndTogglesAnAgent()
    {
        Render<MudPopoverProvider>();
        var cut = Render<InternalAgents>();
        cut.WaitForAssertion(() => Assert.Contains("Model Selector", cut.Markup));

        var copilots = cut.Find("[aria-label='Copilots']");
        Assert.Equal(13, copilots.QuerySelectorAll("tbody tr").Length);
        Assert.Contains("Epic Template Copilot", copilots.TextContent);
        var background = cut.Find("[aria-label='Background agents']");
        Assert.Contains("Chat Suggestions", background.TextContent);

        var modelSelector = cut.FindComponents<MudSwitch<bool>>().Single(s => s.Instance.UserAttributes["aria-label"]?.ToString() == "Enable Model Selector");
        await cut.InvokeAsync(() => modelSelector.Instance.ValueChanged.InvokeAsync(false));

        Assert.False((await Agents.GetAsync(InternalAgentKeys.ModelSelector, _cancellationToken)).IsEnabled);
    }

    [Fact]
    public async Task EditorPage_SavesAndResetsThePrompt()
    {
        await SetModelAsync(InternalAgentKeys.AgentCopilot, "stored-model");
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var cut = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.AgentCopilot));

        Assert.True(cut.FindComponent<MarkdownEditor>().Instance.RawMarkdown);
        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("Be brief."));
        await cut.InvokeAsync(() => Button(cut, "Save").Click());
        cut.WaitForAssertion(() => Assert.Contains("Customized", cut.Markup));
        Assert.Equal("Be brief.", (await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken)).Instructions);

        await cut.InvokeAsync(() => { _ = cut.FindAll("button").Single(b => b.TextContent.Contains("Reset to default")).ClickAsync(new()); });
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Reset").Click());

        cut.WaitForAssertion(() => Assert.False(Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken).Result.IsCustomized));
        Assert.Equal(InternalAgentDefaults.Find(InternalAgentKeys.AgentCopilot)!.Instructions, cut.FindComponent<MarkdownEditor>().Instance.Value);
    }

    [Fact]
    public async Task EditorPage_OnlySavesACopilotModelThatPassedTheTest()
    {
        // The second test finds no scripted response, so the model call fails.
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("OK"));
        Render<MudPopoverProvider>();
        var cut = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.AgentCopilot));
        cut.WaitForAssertion(() => Assert.True(Button(cut, "Save").HasAttribute("disabled")));
        Assert.True(Button(cut, "Test").HasAttribute("disabled"));

        ModelInput(cut).Input("  good-model ");
        Assert.True(Button(cut, "Save").HasAttribute("disabled"));
        await cut.InvokeAsync(() => Button(cut, "Test").Click());
        cut.WaitForAssertion(() => Assert.Contains("The model responded.", cut.Markup));
        Assert.False(Button(cut, "Save").HasAttribute("disabled"));

        ModelInput(cut).Input("untested-model");
        Assert.True(Button(cut, "Save").HasAttribute("disabled"));
        Assert.DoesNotContain("The model responded.", cut.Markup);

        ModelInput(cut).Input("good-model");
        Assert.False(Button(cut, "Save").HasAttribute("disabled"));
        await cut.InvokeAsync(() => Button(cut, "Save").Click());
        cut.WaitForAssertion(() => Assert.Equal("good-model", Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken).Result.Model));

        ModelInput(cut).Input("bad-model");
        await cut.InvokeAsync(() => Button(cut, "Test").Click());
        cut.WaitForAssertion(() => Assert.Contains("mud-alert-outlined-error", cut.Markup));
        Assert.True(Button(cut, "Save").HasAttribute("disabled"));
        Assert.Equal("good-model", (await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken)).Model);
    }

    [Fact]
    public async Task EditorPage_NeedsNoNewTestForAnUnchangedModel_AndHasNoModelFieldForBackgroundAgents()
    {
        await SetModelAsync(InternalAgentKeys.AgentCopilot, "stored-model");
        Render<MudPopoverProvider>();

        var copilot = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.AgentCopilot));
        copilot.WaitForAssertion(() => Assert.False(Button(copilot, "Save").HasAttribute("disabled")));

        var background = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.ModelSelector));
        background.WaitForAssertion(() => Assert.False(Button(background, "Save").HasAttribute("disabled")));
        Assert.DoesNotContain(background.FindAll("button"), b => b.TextContent.Trim() == "Test");
        Assert.Empty(background.FindComponents<MarkdownEditor>());
        Assert.Single(background.FindAll("textarea"));
    }

    [Fact]
    public async Task Save_KeepsTheModelOfACopilot_AndIgnoresItForBackgroundAgents()
    {
        var saved = await SetModelAsync(InternalAgentKeys.AgentCopilot, "  gpt-custom ");
        Assert.Equal("gpt-custom", saved.Model);

        var reset = await Agents.ResetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken);
        Assert.Equal("gpt-custom", reset.Model);

        await Assert.ThrowsAsync<DomainRuleException>(() => Agents.SaveAsync(saved with { Model = " " }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Agents.SaveAsync(
            saved with { Model = new string('a', InternalAgent.MaxModelLength + 1) }, _cancellationToken));
        Assert.Equal("gpt-custom", (await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken)).Model);

        Assert.Equal(string.Empty, (await SetModelAsync(InternalAgentKeys.ModelSelector, "ignored")).Model);
    }

    [Fact]
    public async Task TestModel_ReportsTheFailureOfTheModel_AndRequiresAModel()
    {
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("OK"));

        Assert.Null((await Agents.TestModelAsync("works", _cancellationToken)).Error);
        Assert.False(string.IsNullOrEmpty((await Agents.TestModelAsync("fails", _cancellationToken)).Error));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Agents.TestModelAsync(" ", _cancellationToken));
    }

    [Fact]
    public void EditorPage_ForAnUnknownAgent_ShowsAnError()
    {
        var cut = Render<InternalAgentEditor>(p => p.Add(e => e.Key, "unknown"));

        cut.WaitForAssertion(() => Assert.Contains("This internal agent does not exist.", cut.Markup));
    }

    private async Task<InternalAgent> SetModelAsync(string key, string model)
    {
        return await Agents.SaveAsync(await Agents.GetAsync(key, _cancellationToken) with { Model = model }, _cancellationToken);
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<InternalAgentEditor> cut, string text)
    {
        return cut.FindAll("button").Single(b => b.TextContent.Trim() == text);
    }

    private static AngleSharp.Dom.IElement ModelInput(IRenderedComponent<InternalAgentEditor> cut)
    {
        return cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Model").Find("input");
    }

    /// <summary>The system prompt the agent sent with request <paramref name="index"/>.</summary>
    internal static string SystemPrompt(ScriptedChatClient client, int index)
    {
        return string.Join("\n", new[] { client.Options[index]?.Instructions }
            .Concat(client.Requests[index].Where(m => m.Role == ChatRole.System).Select(m => m.Text))
            .Where(s => !string.IsNullOrEmpty(s)));
    }
}
