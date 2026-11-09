using System.ComponentModel;
using System.Reflection;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Assistant;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Application.Features.InternalModels;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Extensions;
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
    private InternalModelService Models => Services.GetRequiredService<InternalModelService>();

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
        Assert.Equal(17, keys.Count);
        foreach (var type in WorkItemTypeRules.All)
        {
            Assert.Contains("`update_work_item_form`", InternalAgentDefaults.Find(InternalAgentKeys.ForWorkItem(type))!.Instructions);
            Assert.Contains("`update_template_form`", InternalAgentDefaults.Find(InternalAgentKeys.ForWorkItemTemplate(type))!.Instructions);
        }
        Assert.Contains("`update_agent_form`", InternalAgentDefaults.Find(InternalAgentKeys.AgentCopilot)!.Instructions);
        Assert.Contains("This is an agent template", InternalAgentDefaults.Find(InternalAgentKeys.AgentTemplateCopilot)!.Instructions);
        Assert.Equal([InternalAgentKeys.ModelSelector, InternalAgentKeys.ChatSuggestions, InternalAgentKeys.DescriptionWriter, InternalAgentKeys.WorkItemBreakdown],
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
    public async Task EditorPage_ResetLoadsTheStoredVersion_NotTheDefault()
    {
        var stored = await Agents.SaveAsync(await SetModelAsync(InternalAgentKeys.AgentCopilot, "stored-model") with { Instructions = "Stored prompt." }, _cancellationToken);
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var cut = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.AgentCopilot));

        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("Unsaved prompt."));
        await cut.InvokeAsync(() => { _ = Button(cut, "Reset").ClickAsync(new()); });
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent == "Reset").Click());

        cut.WaitForAssertion(() => Assert.Equal(stored.Instructions, cut.FindComponent<MarkdownEditor>().Instance.Value));
    }

    [Fact]
    public async Task EditorPage_OnlySavesACopilotModelThatPassedTheTest()
    {
        var good = await AddModelAsync("good-model");
        var untested = await AddModelAsync("untested-model");
        var bad = await AddModelAsync("bad-model");
        // The second test finds no scripted response, so the model call fails.
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("OK"));
        Render<MudPopoverProvider>();
        var cut = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.AgentCopilot));
        cut.WaitForAssertion(() => Assert.True(Button(cut, "Save").HasAttribute("disabled")));
        Assert.True(Button(cut, "Test").HasAttribute("disabled"));

        await SelectModelAsync(cut, good.Id);
        Assert.True(Button(cut, "Save").HasAttribute("disabled"));
        await cut.InvokeAsync(() => Button(cut, "Test").Click());
        cut.WaitForAssertion(() => Assert.Contains("The model responded.", cut.Markup));
        Assert.False(Button(cut, "Save").HasAttribute("disabled"));

        await SelectModelAsync(cut, untested.Id);
        Assert.True(Button(cut, "Save").HasAttribute("disabled"));
        Assert.DoesNotContain("The model responded.", cut.Markup);

        await SelectModelAsync(cut, good.Id);
        Assert.False(Button(cut, "Save").HasAttribute("disabled"));
        await cut.InvokeAsync(() => Button(cut, "Save").Click());
        cut.WaitForAssertion(() => Assert.Equal("good-model", Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken).Result.Model));

        await SelectModelAsync(cut, bad.Id);
        await cut.InvokeAsync(() => Button(cut, "Test").Click());
        cut.WaitForAssertion(() => Assert.Contains("mud-alert-outlined-error", cut.Markup));
        Assert.True(Button(cut, "Save").HasAttribute("disabled"));
        Assert.Equal("good-model", (await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken)).Model);
    }

    [Fact]
    public async Task EditorPage_OffersTheCatalogInASelect_ForCopilotsAndBackgroundAgents()
    {
        var model = await AddModelAsync("gpt-listed", input: 2.5m, output: 10m);
        await SetModelAsync(InternalAgentKeys.AgentCopilot, "gpt-listed");
        Render<MudPopoverProvider>();

        var copilot = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.AgentCopilot));
        copilot.WaitForAssertion(() => Assert.False(Button(copilot, "Save").HasAttribute("disabled")));
        Assert.Equal("Label of gpt-listed — €2.500 in / €10.000 out", ModelSelect(copilot).Instance.ToStringFunc!(model.Id));
        Assert.DoesNotContain(copilot.FindComponents<MudTextField<string>>(), f => f.Instance.Label == "Model");

        // A background agent may stay on the configured model, but a model it does choose must pass the test too.
        var background = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.ModelSelector));
        background.WaitForAssertion(() => Assert.False(Button(background, "Save").HasAttribute("disabled")));
        Assert.True(background.FindComponent<MarkdownEditor>().Instance.RawMarkdown);
        await SelectModelAsync(background, model.Id);
        Assert.True(Button(background, "Save").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Save_KeepsTheModelOfACopilot_AndLinksBackgroundAgentsToo()
    {
        var saved = await SetModelAsync(InternalAgentKeys.AgentCopilot, "gpt-custom");
        Assert.Equal("gpt-custom", saved.Model);

        var reset = await Agents.ResetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken);
        Assert.Equal("gpt-custom", reset.Model);

        await Assert.ThrowsAsync<DomainRuleException>(() => Agents.SaveAsync(saved with { ModelId = null }, _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => Agents.SaveAsync(saved with { ModelId = Guid.NewGuid() }, _cancellationToken));
        Assert.Equal("gpt-custom", (await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken)).Model);

        var background = await SetModelAsync(InternalAgentKeys.ModelSelector, "gpt-custom");
        Assert.Equal("gpt-custom", background.Model);
        Assert.Empty((await Agents.SaveAsync(background with { ModelId = null }, _cancellationToken)).Model);
    }

    [Fact]
    public async Task Catalog_ValidatesItsModels_AndRefusesDeletingAModelInUse()
    {
        var model = await AddModelAsync("  gpt-catalog ", input: 1m, output: 2m);
        Assert.Equal("gpt-catalog", model.Name);

        Assert.Equal("Label of gpt-catalog", model.Label);

        await Assert.ThrowsAsync<DomainRuleException>(() => Models.SaveAsync(new InternalModel { Name = "gpt-catalog", Label = "Another" }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Models.SaveAsync(new InternalModel { Name = " ", Label = "Blank model" }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Models.SaveAsync(new InternalModel { Name = "no-label", Label = " " }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Models.SaveAsync(
            new InternalModel { Name = "long-label", Label = new string('a', InternalModel.MaxLabelLength + 1) }, _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Models.SaveAsync(new InternalModel { Name = "x", InputPricePerMillion = -1 }, _cancellationToken));

        await SetModelAsync(InternalAgentKeys.AgentCopilot, "gpt-catalog");
        var inUse = await Assert.ThrowsAsync<DomainRuleException>(() => Models.DeleteAsync(model.Id, _cancellationToken));
        Assert.Contains("Agent Copilot", inUse.Message);

        var other = await AddModelAsync("gpt-other");
        await Models.DeleteAsync(other.Id, _cancellationToken);
        Assert.Equal(["gpt-catalog"], (await Models.GetAllAsync(_cancellationToken)).Select(m => m.Name));
    }

    [Fact]
    public void Sources_AllHaveADescription_AndEveryCopilotHasItsOwnChat()
    {
        Assert.All(typeof(InternalAgentUsageSource).GetFields(BindingFlags.Public | BindingFlags.Static),
            field => Assert.NotNull(field.GetCustomAttribute<DescriptionAttribute>()));
        Assert.Equal("Epic Copilot Chat", InternalAgentUsageSource.EpicCopilotChat.Description());

        var chats = InternalAgentDefaults.All.Where(a => a.Kind == InternalAgentKind.Copilot).Select(a => InternalAgentUsageSources.ForCopilot(a.Key)).ToList();
        Assert.DoesNotContain(InternalAgentUsageSource.Unknown, chats);
        Assert.Equal(chats.Count, chats.Distinct().Count());
        Assert.Equal(InternalAgentUsageSource.EpicCopilotChat, InternalAgentUsageSources.ForCopilot(InternalAgentKeys.ForWorkItem(WorkItemType.Epic)));
    }

    [Fact]
    public async Task ListPage_AddsUpAllAgentsInTiles_AndListsTheRequestsOfAllAgents()
    {
        await AddModelAsync("gpt-all", input: 1m, output: 0m);
        await Agents.SeedAsync(_cancellationToken);
        var store = Services.GetRequiredService<IInternalAgentStore>();
        await store.RecordUsageAsync(InternalAgentKeys.ModelSelector, "gpt-all", new ModelUsage(1_000_000, 0), InternalAgentUsageSource.ModelSelector, _cancellationToken);
        await store.RecordUsageAsync(InternalAgentKeys.AgentCopilot, "gpt-all", new ModelUsage(2_000_000, 0), InternalAgentUsageSource.AgentCopilotChat, _cancellationToken);
        Render<MudPopoverProvider>();

        var cut = Render<InternalAgents>();

        cut.WaitForAssertion(() => Assert.Equal("€3.000", cut.Find(".usage-total .usage-value").TextContent.Trim()));
        Assert.Equal("2", cut.Find(".usage-requests .usage-value").TextContent.Trim());
        var rows = cut.Find("[aria-label='All recent requests']").QuerySelectorAll("tbody tr");
        Assert.Equal(2, rows.Length);
        Assert.Contains("Agent Copilot Chat", rows[0].TextContent);
        Assert.Equal("Agent Copilot", rows[0].QuerySelector("td[data-label='Agent']")!.TextContent.Trim());
        Assert.Equal("Model Selector", rows[1].QuerySelector("td[data-label='Source']")!.TextContent.Trim());
    }

    [Fact]
    public async Task EditorPage_ListsOnlyTheRequestsOfThatAgent_WithoutAnAgentColumn()
    {
        await Agents.SeedAsync(_cancellationToken);
        var store = Services.GetRequiredService<IInternalAgentStore>();
        await store.RecordUsageAsync(InternalAgentKeys.ModelSelector, "m", new ModelUsage(1, 0), InternalAgentUsageSource.ModelSelector, _cancellationToken);
        await store.RecordUsageAsync(InternalAgentKeys.AgentCopilot, "m", new ModelUsage(1, 0), InternalAgentUsageSource.AgentCopilotChat, _cancellationToken);
        Render<MudPopoverProvider>();

        var cut = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.ModelSelector));

        cut.WaitForAssertion(() => Assert.Single(cut.Find("[aria-label='Recent requests']").QuerySelectorAll("tbody tr")));
        Assert.Empty(cut.Find("[aria-label='Recent requests']").QuerySelectorAll("td[data-label='Agent']"));
    }

    [Fact]
    public void PricesAndCosts_AreShownInEuroWithThreeDecimals()
    {
        Assert.Equal("€0.004", AiModel.FormatPrice(0.0042m));
        Assert.Equal("€1,234.500", AiModel.FormatPrice(1234.5m));
        Assert.Equal("€0.000", AiModel.FormatPrice(0m));
        Assert.Equal("–", AiModel.FormatPrice(null));
        Assert.Equal("€2.500", InternalAgentUsage.FormatCost(2.5m));
        Assert.Equal("€0.000", InternalAgentUsage.FormatCost(0.00004m));
    }

    [Fact]
    public async Task DefaultModel_IsUnique_WhileAnyNumberOfModelsCanBeFlagships()
    {
        var first = await AddDefaultModelAsync("a-model", isFlagship: true);
        await AddDefaultModelAsync("b-model", isFlagship: true);

        Assert.Equal([("a-model", false, true), ("b-model", true, true)],
            (await Models.GetAllAsync(_cancellationToken)).Select(m => (m.Name, m.IsDefault, m.IsFlagship)));

        await Models.SaveAsync(first with { IsDefault = true }, _cancellationToken);
        Assert.Equal(["a-model"], (await Models.GetAllAsync(_cancellationToken)).Where(m => m.IsDefault).Select(m => m.Name));
    }

    [Fact]
    public async Task Seeding_LinksNewAgentsToTheDefaultModel_AndLeavesExistingOnesAlone()
    {
        var store = Services.GetRequiredService<IInternalAgentStore>();
        await AddDefaultModelAsync("first-default");
        await store.EnsureAsync([InternalAgentDefaults.Find(InternalAgentKeys.AgentCopilot)!], _cancellationToken);

        await AddDefaultModelAsync("second-default");
        await store.EnsureAsync([InternalAgentDefaults.Find(InternalAgentKeys.AgentCopilot)!, InternalAgentDefaults.Find(InternalAgentKeys.ModelSelector)!], _cancellationToken);

        Assert.Equal("first-default", (await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken)).Model);
        Assert.Equal("second-default", (await Agents.GetAsync(InternalAgentKeys.ModelSelector, _cancellationToken)).Model);
    }

    [Fact]
    public async Task EditorPage_PreselectsTheDefaultModel_UnlessABackgroundAgentWasSavedWithoutOne()
    {
        await Agents.SeedAsync(_cancellationToken);
        var savedWithoutModel = await Agents.SaveAsync(await Agents.GetAsync(InternalAgentKeys.ChatSuggestions, _cancellationToken) with { Instructions = "Changed." }, _cancellationToken);
        Assert.Null(savedWithoutModel.ModelId);
        var model = await AddDefaultModelAsync("the-default");
        Render<MudPopoverProvider>();

        var copilot = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.AgentCopilot));
        copilot.WaitForAssertion(() => Assert.Equal(model.Id, ModelSelect(copilot).Instance.GetState(s => s.Value)));

        var background = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.ModelSelector));
        background.WaitForAssertion(() => Assert.Equal(model.Id, ModelSelect(background).Instance.GetState(s => s.Value)));

        var saved = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.ChatSuggestions));
        saved.WaitForAssertion(() => Assert.Null(ModelSelect(saved).Instance.GetState(s => s.Value)));
    }

    [Fact]
    public async Task ModelsPage_ListsTheCatalog_AndAddsAModel()
    {
        await AddModelAsync("gpt-priced", input: 2.5m, output: 10m);
        await Models.SaveAsync(new InternalModel { Name = "gpt-free", Label = "Free model", IsFlagship = true, IsDefault = true }, _cancellationToken);
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();

        var cut = Render<InternalModels>();

        cut.WaitForAssertion(() => Assert.Contains("gpt-priced", cut.Markup));
        var rows = cut.FindAll("tbody tr");
        var freeRow = rows.Single(r => r.TextContent.Contains("gpt-free"));
        Assert.Equal("Flagship", freeRow.QuerySelector("td[data-label='Flagship']")!.TextContent.Trim());
        Assert.Equal("Default", freeRow.QuerySelector("td[data-label='Default']")!.TextContent.Trim());
        Assert.DoesNotContain("Flagship", freeRow.QuerySelector("td[data-label='Model']")!.TextContent);
        var free = freeRow.TextContent;
        var priced = rows.Single(r => r.TextContent.Contains("gpt-priced")).TextContent;
        Assert.All(new[] { "No price", "Flagship", "Default" }, chip =>
        {
            Assert.Contains(chip, free);
            Assert.DoesNotContain(chip, priced);
        });

        await cut.InvokeAsync(() => { _ = cut.FindAll("button").Single(b => b.TextContent.Contains("New model")).ClickAsync(new()); });
        dialogs.WaitForAssertion(() => Assert.Contains("Description", dialogs.Markup));
        dialogs.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Model").Find("input").Change("gpt-new");
        dialogs.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Label").Find("input").Change("New model");
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click());

        cut.WaitForAssertion(() => Assert.Contains("New model", cut.Find("[aria-label='Internal models']").TextContent));
        Assert.Contains("gpt-new", cut.Find("[aria-label='Internal models']").TextContent);
    }

    [Fact]
    public async Task BackgroundAgent_RunsOnItsModel_AndAddsTheCostOfEachRequest()
    {
        await SetModelAsync(InternalAgentKeys.ChatSuggestions, "gpt-background", input: 2m, output: 8m);
        _chatClient = new ScriptedChatClient(ScriptedChatClient.WithUsage(ScriptedChatClient.Text("""{"suggestions":["One"]}"""), 1_000, 500));
        var assistant = Services.GetRequiredService<AssistantService>();
        var copilot = await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken);

        await assistant.SuggestPromptsAsync(copilot, null, null, _cancellationToken);

        var agent = await Agents.GetAsync(InternalAgentKeys.ChatSuggestions, _cancellationToken);
        Assert.Equal((1L, 1_000L, 500L, 0.002m, 0.004m, 0.006m),
            (agent.RequestCount, agent.TotalInputTokens, agent.TotalOutputTokens, agent.TotalInputCost, agent.TotalOutputCost, agent.TotalCost));
        var usage = Assert.Single(await Agents.GetUsageAsync(InternalAgentKeys.ChatSuggestions, _cancellationToken));
        Assert.Equal(("gpt-background", 2m, 8m, 0.006m), (usage.Model, usage.InputPricePerMillion, usage.OutputPricePerMillion, usage.TotalCost));
        Assert.Equal(InternalAgentUsageSource.ChatSuggestions, usage.Source);
    }

    [Fact]
    public async Task Copilot_RecordsEveryModelRequestOfATurn_WithStreamedUsage()
    {
        await SetModelAsync(InternalAgentKeys.AgentCopilot, "gpt-chat", input: 1m, output: 4m);
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.WithUsage(ScriptedChatClient.ToolCall("update_template_form", new Dictionary<string, object?> { ["name"] = "Crash report" }), 100, 10),
            ScriptedChatClient.WithUsage(ScriptedChatClient.Text("Done."), 200, 20, cachedInputTokens: 50, reasoningTokens: 15));
        var tools = AssistantForms.ForWorkItemTemplate(() => new WorkItemTemplate(), action =>
        {
            action();
            return Task.CompletedTask;
        });
        var assistant = Services.GetRequiredService<AssistantService>();
        var conversation = assistant.CreateConversation(await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken), tools);

        await foreach (var _ in conversation.RunStreamingAsync("Go", _cancellationToken))
        {
        }

        var agent = await Agents.GetAsync(InternalAgentKeys.AgentCopilot, _cancellationToken);
        Assert.Equal((2L, 300L, 30L, 50L, 15L), (agent.RequestCount, agent.TotalInputTokens, agent.TotalOutputTokens, agent.TotalCachedInputTokens, agent.TotalReasoningTokens));
        var requests = await Agents.GetUsageAsync(InternalAgentKeys.AgentCopilot, _cancellationToken);
        Assert.Equal([200L, 100L], requests.Select(u => u.InputTokens));
        Assert.All(requests, u => Assert.Equal(InternalAgentUsageSource.AgentCopilotChat, u.Source));
    }

    [Fact]
    public async Task PriceChange_LeavesPastCostsAlone_AndAppliesToNewRequests()
    {
        var model = await AddModelAsync("gpt-repriced", input: 1m, output: 1m);
        var key = InternalAgentKeys.AgentCopilot;
        await Agents.SaveAsync(await Agents.GetAsync(key, _cancellationToken) with { ModelId = model.Id }, _cancellationToken);
        var store = Services.GetRequiredService<IInternalAgentStore>();
        await store.RecordUsageAsync(key, "gpt-repriced", new ModelUsage(1_000_000, 0), InternalAgentUsageSource.ModelTest, _cancellationToken);

        await Models.SaveAsync(model with { InputPricePerMillion = 50m }, _cancellationToken);

        Assert.Equal(1m, (await Agents.GetAsync(key, _cancellationToken)).TotalCost);
        await store.RecordUsageAsync(key, "gpt-repriced", new ModelUsage(1_000_000, 0), InternalAgentUsageSource.ModelTest, _cancellationToken);
        Assert.Equal(51m, (await Agents.GetAsync(key, _cancellationToken)).TotalCost);
        Assert.Equal([50m, 1m], (await Agents.GetUsageAsync(key, _cancellationToken)).Select(u => u.InputCost));
    }

    [Fact]
    public async Task CachedInputTokens_UseTheCachedPrice_OrTheInputPriceWithoutOne()
    {
        var store = Services.GetRequiredService<IInternalAgentStore>();
        await Agents.SeedAsync(_cancellationToken);
        await Models.SaveAsync(new InternalModel { Name = "with-cache-price", Label = "With", InputPricePerMillion = 10m, CachedInputPricePerMillion = 1m }, _cancellationToken);
        await Models.SaveAsync(new InternalModel { Name = "without-cache-price", Label = "Without", InputPricePerMillion = 10m }, _cancellationToken);
        await Assert.ThrowsAsync<ValidationFailedException>(() => Models.SaveAsync(new InternalModel { Name = "x", CachedInputPricePerMillion = -1 }, _cancellationToken));

        await store.RecordUsageAsync(InternalAgentKeys.ModelSelector, "with-cache-price", new ModelUsage(1_000_000, 0, 400_000), InternalAgentUsageSource.ModelTest, _cancellationToken);
        await store.RecordUsageAsync(InternalAgentKeys.ModelSelector, "without-cache-price", new ModelUsage(1_000_000, 0, 400_000), InternalAgentUsageSource.ModelTest, _cancellationToken);

        var agent = await Agents.GetAsync(InternalAgentKeys.ModelSelector, _cancellationToken);
        Assert.Equal((2_000_000L, 800_000L, 16.4m), (agent.TotalInputTokens, agent.TotalCachedInputTokens, agent.TotalInputCost));
        var usage = await Agents.GetUsageAsync(InternalAgentKeys.ModelSelector, _cancellationToken);
        Assert.Equal([(10m, 10m), (1m, 6.4m)], usage.Select(u => (u.CachedInputPricePerMillion, u.InputCost)));
    }

    [Fact]
    public async Task ReasoningTokens_AreKeptForInformation_AndNotChargedOnTopOfTheOutput()
    {
        await Agents.SeedAsync(_cancellationToken);
        await AddModelAsync("reasoner", input: 0m, output: 1m);
        var store = Services.GetRequiredService<IInternalAgentStore>();

        await store.RecordUsageAsync(InternalAgentKeys.ModelSelector, "reasoner", new ModelUsage(0, 1_000_000, 0, 400_000), InternalAgentUsageSource.ModelTest, _cancellationToken);

        var agent = await Agents.GetAsync(InternalAgentKeys.ModelSelector, _cancellationToken);
        Assert.Equal((1_000_000L, 400_000L, 1m), (agent.TotalOutputTokens, agent.TotalReasoningTokens, agent.TotalCost));
        var usage = Assert.Single(await Agents.GetUsageAsync(InternalAgentKeys.ModelSelector, _cancellationToken));
        Assert.Equal((400_000L, 1m), (usage.ReasoningTokens, usage.OutputCost));
    }

    [Fact]
    public async Task AgentWithoutAModel_IsPricedAsTheConfiguredModel_OrRecordedWithoutACost()
    {
        var store = Services.GetRequiredService<IInternalAgentStore>();
        await Agents.SeedAsync(_cancellationToken);
        await AddModelAsync("known", input: 1m, output: 1m);

        await store.RecordUsageAsync(InternalAgentKeys.ModelSelector, "known", new ModelUsage(1_000_000, 0), InternalAgentUsageSource.ModelTest, _cancellationToken);
        await store.RecordUsageAsync(InternalAgentKeys.ModelSelector, "not-in-the-catalog", new ModelUsage(1_000_000, 0), InternalAgentUsageSource.ModelTest, _cancellationToken);

        var agent = await Agents.GetAsync(InternalAgentKeys.ModelSelector, _cancellationToken);
        Assert.Equal((2L, 2_000_000L, 1m), (agent.RequestCount, agent.TotalInputTokens, agent.TotalCost));
    }

    [Fact]
    public async Task ListAndEditorPage_ShowTheRunningCost()
    {
        var model = await AddModelAsync("gpt-shown", input: 1m, output: 1m);
        await Agents.SeedAsync(_cancellationToken);
        await Services.GetRequiredService<IInternalAgentStore>().RecordUsageAsync(InternalAgentKeys.ModelSelector, model.Name, new ModelUsage(1_500_000, 0), InternalAgentUsageSource.ModelTest, _cancellationToken);
        await SetModelAsync(InternalAgentKeys.ModelSelector, "gpt-shown");
        Render<MudPopoverProvider>();

        var list = Render<InternalAgents>();
        list.WaitForAssertion(() => Assert.Contains("€1.500",list.Find("[aria-label='Background agents']").TextContent));
        Assert.Contains("Label of gpt-shown", list.Find("[aria-label='Background agents']").TextContent);

        var editor = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.ModelSelector));
        editor.WaitForAssertion(() => Assert.Contains("€1.500",editor.Find("[aria-label='Usage']").TextContent));
        Assert.Contains("gpt-shown", editor.Find("[aria-label='Recent requests']").TextContent);
        Assert.Contains("Of which reasoning", editor.Find("[aria-label='Recent requests']").TextContent);
    }

    [Fact]
    public async Task TestModel_ReportsTheFailureOfTheModel_AndRequiresAModel()
    {
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text("OK"));

        Assert.Null((await Agents.TestModelAsync(InternalAgentKeys.AgentCopilot, "works", _cancellationToken)).Error);
        Assert.False(string.IsNullOrEmpty((await Agents.TestModelAsync(InternalAgentKeys.AgentCopilot, "fails", _cancellationToken)).Error));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Agents.TestModelAsync(InternalAgentKeys.AgentCopilot, " ", _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Agents.TestModelAsync("unknown", "works", _cancellationToken));
    }

    [Fact]
    public async Task TestModel_CountsTowardsTheUsageOfTheAgent_AtThePriceOfTheTestedModel()
    {
        await AddModelAsync("tested", input: 2m, output: 0m);
        _chatClient = new ScriptedChatClient(ScriptedChatClient.WithUsage(ScriptedChatClient.Text("OK"), 1_000_000, 0));

        await Agents.TestModelAsync(InternalAgentKeys.ModelSelector, "tested", _cancellationToken);
        // Nothing is scripted anymore, so this request fails and uses no tokens.
        await Agents.TestModelAsync(InternalAgentKeys.ModelSelector, "tested", _cancellationToken);

        var agent = await Agents.GetAsync(InternalAgentKeys.ModelSelector, _cancellationToken);
        Assert.Equal((1L, 1_000_000L, 2m), (agent.RequestCount, agent.TotalInputTokens, agent.TotalCost));
        Assert.Null(agent.ModelId);
        var usage = Assert.Single(await Agents.GetUsageAsync(InternalAgentKeys.ModelSelector, _cancellationToken));
        Assert.Equal(("tested", InternalAgentUsageSource.ModelTest), (usage.Model, usage.Source));
    }

    [Fact]
    public async Task EditorPage_ShowsTheUsageOfATestInColoredStats_AndKeepsUnsavedChanges()
    {
        var model = await AddModelAsync("tested", input: 2m, output: 0m);
        _chatClient = new ScriptedChatClient(ScriptedChatClient.WithUsage(ScriptedChatClient.Text("OK"), 1_000_000, 0));
        Render<MudPopoverProvider>();
        var cut = Render<InternalAgentEditor>(p => p.Add(e => e.Key, InternalAgentKeys.AgentCopilot));
        cut.WaitForAssertion(() => Assert.Contains("€0.000", cut.Find("[aria-label='Usage']").TextContent));
        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("Unsaved prompt."));

        await SelectModelAsync(cut, model.Id);
        await cut.InvokeAsync(() => Button(cut, "Test").Click());

        cut.WaitForAssertion(() => Assert.Equal("€2.000", cut.Find(".usage-input .usage-value").TextContent.Trim()));
        Assert.Equal("1", cut.Find(".usage-requests .usage-value").TextContent.Trim());
        Assert.Contains("tested", cut.Find("[aria-label='Recent requests']").TextContent);
        Assert.Contains("Model test", cut.Find("[aria-label='Recent requests']").TextContent);
        Assert.Equal("Unsaved prompt.", cut.FindComponent<MarkdownEditor>().Instance.Value);
    }

    [Fact]
    public void EditorPage_ForAnUnknownAgent_ShowsAnError()
    {
        var cut = Render<InternalAgentEditor>(p => p.Add(e => e.Key, "unknown"));

        cut.WaitForAssertion(() => Assert.Contains("This internal agent does not exist.", cut.Markup));
    }

    private async Task<InternalAgent> SetModelAsync(string key, string model, decimal input = 1m, decimal output = 1m)
    {
        var existing = (await Models.GetAllAsync(_cancellationToken)).SingleOrDefault(m => m.Name == model) ?? await AddModelAsync(model, input, output);
        return await Agents.SaveAsync(await Agents.GetAsync(key, _cancellationToken) with { ModelId = existing.Id }, _cancellationToken);
    }

    private async Task<InternalModel> AddModelAsync(string name, decimal input = 1m, decimal output = 1m)
    {
        return await Models.SaveAsync(new InternalModel { Name = name, Label = $"Label of {name.Trim()}", InputPricePerMillion = input, OutputPricePerMillion = output }, _cancellationToken);
    }

    private async Task<InternalModel> AddDefaultModelAsync(string name, bool isFlagship = false)
    {
        return await Models.SaveAsync(new InternalModel { Name = name, Label = name, IsDefault = true, IsFlagship = isFlagship }, _cancellationToken);
    }

    private static async Task SelectModelAsync(IRenderedComponent<InternalAgentEditor> cut, Guid id)
    {
        await cut.InvokeAsync(() => ModelSelect(cut).Instance.ValueChanged.InvokeAsync(id));
    }

    private static IRenderedComponent<MudSelect<Guid?>> ModelSelect(IRenderedComponent<InternalAgentEditor> cut)
    {
        return cut.FindComponents<MudSelect<Guid?>>().Single(s => s.Instance.Label == "Model");
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<InternalAgentEditor> cut, string text)
    {
        return cut.FindAll("button").Single(b => b.TextContent.Trim() == text);
    }

    /// <summary>The system prompt the agent sent with request <paramref name="index"/>.</summary>
    internal static string SystemPrompt(ScriptedChatClient client, int index)
    {
        return string.Join("\n", new[] { client.Options[index]?.Instructions }
            .Concat(client.Requests[index].Where(m => m.Role == ChatRole.System).Select(m => m.Text))
            .Where(s => !string.IsNullOrEmpty(s)));
    }
}
