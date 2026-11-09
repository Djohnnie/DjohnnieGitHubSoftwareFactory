using System.Text.Json;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    [Fact]
    public void TypeRules_SplitEpicsIntoFeatures_FeaturesIntoStories_AndStoriesIntoTasks()
    {
        Assert.Equal(WorkItemType.Feature, WorkItemType.Epic.GeneratedChildType);
        Assert.Equal(WorkItemType.Story, WorkItemType.Feature.GeneratedChildType);
        Assert.Equal(WorkItemType.Task, WorkItemType.Story.GeneratedChildType);
        Assert.Null(WorkItemType.Bug.GeneratedChildType);
        Assert.Null(WorkItemType.Task.GeneratedChildType);
    }

    [Fact]
    public async Task GenerateChildren_SendsParentDefaultTemplateAndExistingChildren_AndStoresNothing()
    {
        var chat = new ScriptedChatClient(Proposals(("Payment", "## Goal\nPay.")));
        await using var services = await ServicesWithChatAsync(chat);
        var items = services.GetRequiredService<WorkItemService>();
        var templates = services.GetRequiredService<WorkItemTemplateService>();
        await templates.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Feature, Name = "A first alphabetically", TitleTemplate = "Other", Description = "## Not this one" }, _cancellationToken);
        await templates.SaveAsync(new WorkItemTemplate { Type = WorkItemType.Feature, Name = "Standard", TitleTemplate = "Add [capability]", Description = "## Default skeleton", IsDefault = true }, _cancellationToken);
        var epic = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Webshop", Description = "Sell things online." }, _cancellationToken);
        await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Feature, ParentId = epic.Id, Title = "Existing cart", Description = "Cart." }, _cancellationToken);
        await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, ParentId = epic.Id, Title = "Unrelated bug", Description = "Bug." }, _cancellationToken);

        var result = await items.GenerateChildrenAsync(Repo, epic.Id, _cancellationToken);

        Assert.Equal(WorkItemType.Feature, result.ChildType);
        Assert.Equal("Standard", result.TemplateName);
        Assert.Equal(new GeneratedWorkItem("Payment", "## Goal\nPay."), Assert.Single(result.Items));
        var input = string.Join("\n", Assert.Single(chat.Requests).Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        Assert.Contains("Sell things online.", input);
        Assert.Contains("## Default skeleton", input);
        Assert.DoesNotContain("## Not this one", input);
        Assert.Contains("Existing cart", input);
        Assert.DoesNotContain("Unrelated bug", input);
        Assert.Contains("You are the Work Item Breakdown", InternalAgentTests.SystemPrompt(chat, 0));
        Assert.Equal(2, (await items.GetAllAsync(Repo, parentId: epic.Id, cancellationToken: _cancellationToken)).Count);
    }

    [Fact]
    public async Task GenerateChildren_DropsBlankAndDuplicateItems_AndCutsOffOversizedOnes()
    {
        var many = Enumerable.Range(0, GeneratedWorkItem.MaxPerRun + 5).Select(i => ($"Extra {i}", "Body"));
        var chat = new ScriptedChatClient(Proposals(
        [
            ("  ", "No title"),
            ("Existing CART", "Already there"),
            (new string('t', 300), "Long title"),
            ("Twice", "First"),
            ("twice", "Second"),
            .. many
        ]));
        await using var services = await ServicesWithChatAsync(chat);
        var items = services.GetRequiredService<WorkItemService>();
        var epic = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Webshop", Description = "Sell." }, _cancellationToken);
        await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Feature, ParentId = epic.Id, Title = "Existing cart", Description = "Cart." }, _cancellationToken);

        var result = await items.GenerateChildrenAsync(Repo, epic.Id, _cancellationToken);

        Assert.Equal(GeneratedWorkItem.MaxPerRun, result.Items.Count);
        Assert.Equal(GeneratedWorkItem.MaxTitleLength, result.Items[0].Title.Length);
        Assert.Equal("First", Assert.Single(result.Items, i => i.Title.Equals("twice", StringComparison.OrdinalIgnoreCase)).Description);
        Assert.DoesNotContain(result.Items, i => i.Title.Trim().Length == 0 || i.Title.Equals("Existing cart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateChildren_IsRefused_ForTypesThatAreNotSplit_EmptyParents_AndADisabledAgent()
    {
        var chat = new ScriptedChatClient();
        await using var services = await ServicesWithChatAsync(chat);
        var items = services.GetRequiredService<WorkItemService>();
        var bug = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Bug", Description = "Broken." }, _cancellationToken);
        var empty = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Empty" }, _cancellationToken);
        var epic = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Epic", Description = "Scope." }, _cancellationToken);

        await Assert.ThrowsAsync<DomainRuleException>(() => items.GenerateChildrenAsync(Repo, bug.Id, _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => items.GenerateChildrenAsync(Repo, empty.Id, _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => items.GenerateChildrenAsync("owner/other", epic.Id, _cancellationToken));
        await InternalAgentTests.DisableAsync(services, InternalAgentKeys.WorkItemBreakdown);
        await Assert.ThrowsAsync<DomainRuleException>(() => items.GenerateChildrenAsync(Repo, epic.Id, _cancellationToken));
        Assert.Empty(chat.Requests);
    }

    [Fact]
    public async Task GenerateChildren_WhenTheAgentFails_ReportsItAsARefusal()
    {
        await using var services = await ServicesWithChatAsync(new ScriptedChatClient());
        var items = services.GetRequiredService<WorkItemService>();
        var story = await items.SaveAsync(new WorkItem
        {
            RepositoryFullName = Repo, Type = WorkItemType.Story, Title = "Story", Description = "Scope.",
            ParentId = (await SaveFeatureAsync(items)).Id
        }, _cancellationToken);

        var refusal = await Assert.ThrowsAsync<DomainRuleException>(() => items.GenerateChildrenAsync(Repo, story.Id, _cancellationToken));

        Assert.Contains("could not generate tasks", refusal.Message);
    }

    [Fact]
    public async Task SaveGeneratedChildren_CreatesDraftsOfTheChildTypeUnderTheParent()
    {
        var feature = await SaveFeatureAsync(_service);

        var saved = await _service.SaveGeneratedChildrenAsync(Repo, feature.Id, [new(" Browse ", "## Browse"), new("Filter", "## Filter")], _cancellationToken);

        Assert.Equal(["Browse", "Filter"], saved.Select(s => s.Title));
        var stored = await _service.GetAllAsync(Repo, [WorkItemType.Story], feature.Id, _cancellationToken);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, s => Assert.Equal((WorkItemStatus.Draft, feature.Id, (int?)null), (s.Status, s.ParentId!.Value, s.IssueNumber)));
        Assert.Equal("## Filter", Assert.Single(stored, s => s.Title == "Filter").Description);
    }

    [Fact]
    public async Task SaveGeneratedChildren_StoresNothing_WhenAnyItemIsInvalidOrTheParentCannotBeSplit()
    {
        var feature = await SaveFeatureAsync(_service);
        var bug = await _service.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Bug, Title = "Bug", Description = "Broken." }, _cancellationToken);

        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.SaveGeneratedChildrenAsync(Repo, feature.Id, [new("Good", "Body"), new(" ", "No title")], _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.SaveGeneratedChildrenAsync(Repo, feature.Id, [], _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.SaveGeneratedChildrenAsync(Repo, feature.Id,
            Enumerable.Range(0, GeneratedWorkItem.MaxPerRun + 1).Select(i => new GeneratedWorkItem($"Item {i}", "Body")).ToList(), _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => _service.SaveGeneratedChildrenAsync(Repo, bug.Id, [new("Good", "Body")], _cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => _service.SaveGeneratedChildrenAsync("owner/other", feature.Id, [new("Good", "Body")], _cancellationToken));

        Assert.Empty(await _service.GetAllAsync(Repo, [WorkItemType.Story], cancellationToken: _cancellationToken));
    }

    private async Task<WorkItem> SaveFeatureAsync(WorkItemService items)
    {
        var epic = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Epic", Description = "Scope." }, _cancellationToken);
        return await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Feature, ParentId = epic.Id, Title = "Feature", Description = "Scope." }, _cancellationToken);
    }

    private static ChatResponse Proposals(params IEnumerable<(string Title, string Description)> items)
    {
        return ScriptedChatClient.Text(JsonSerializer.Serialize(new { items = items.Select(i => new { title = i.Title, description = i.Description }) }));
    }
}
