using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Application.Features.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>
/// Describes one feature that <see cref="AnalyzeRepositoryHandler"/> saved and adds its user stories (and, for
/// improvements, bugs) as drafts. A retry doesn't repeat the children an earlier attempt already saved.
/// </summary>
public sealed class WriteFeatureWorkItemsHandler(
    RepositoryFiles repositoryFiles,
    IWorkItemStore items,
    IWorkItemTemplateStore templates,
    ITodoTaskStore tasks,
    InternalAgentRegistry registry,
    BackgroundAgents agents) : ITodoTaskHandler
{
    public TodoTaskKind Kind
    {
        get
        {
            return TodoTaskKind.WriteFeatureWorkItems;
        }
    }

    public async Task<TodoTaskOutcome> ExecuteAsync(TodoTask task, CancellationToken cancellationToken)
    {
        var payload = TodoTaskPayloads.Read<WriteFeatureWorkItemsPayload>(task);
        var agent = await AnalyzeRepositoryHandler.RequireAgentAsync(registry, agents, InternalAgentKeys.RepositoryWorkItemWriter, cancellationToken);
        var repository = task.RepositoryFullName;
        var feature = await items.GetAsync(repository, payload.FeatureId, cancellationToken)
            ?? throw new DomainRuleException($"The feature '{payload.Feature.Title}' was deleted.");
        var epic = await items.GetAsync(repository, payload.EpicId, cancellationToken);
        var snapshot = await repositoryFiles.ReadAsync(repository, payload.PathPrefix, cancellationToken);

        var existingChildren = await items.GetAllAsync(repository, parentId: feature.Id, cancellationToken: cancellationToken);
        var tools = snapshot.CreateTools(RepositoryExplorerTools.DefaultMaxReads);
        var input = new FeatureWritingInput(
            repository,
            payload.Mode,
            epic?.Title ?? feature.Parent?.Title ?? string.Empty,
            epic?.Description ?? string.Empty,
            payload.Feature,
            snapshot.Outline,
            payload.OtherFeatures,
            existingChildren.Select(child => child.Title).ToList(),
            await ProposedAsync(WorkItemType.Feature, cancellationToken),
            await ProposedAsync(WorkItemType.Story, cancellationToken),
            payload.Mode == RepositoryAnalysisMode.Improvements ? await ProposedAsync(WorkItemType.Bug, cancellationToken) : null);
        var written = await agents.WriteFeatureWorkItemsAsync(agent, input, tools, cancellationToken);

        if (await tasks.GetStatusAsync(task.Id, cancellationToken) != TodoTaskStatus.Running)
        {
            return TodoTaskOutcome.Done("Cancelled before the work items were saved.");
        }

        var described = false;
        if (written.Description.Length > 0 && feature.IsDraft)
        {
            feature.Description = written.Description;
            await items.SaveAsync(feature, cancellationToken);
            described = true;
        }
        if (written.Stories.Count > 0)
        {
            await items.AddGeneratedAsync(repository, feature.Id, WorkItemType.Story, written.Stories, task.RootId, cancellationToken);
        }
        if (written.Bugs.Count > 0)
        {
            await items.AddGeneratedAsync(repository, feature.Id, WorkItemType.Bug, written.Bugs, task.RootId, cancellationToken);
        }

        var bugs = payload.Mode == RepositoryAnalysisMode.Improvements ? $" and {Count(written.Bugs.Count, "bug")}" : string.Empty;
        return TodoTaskOutcome.Done(
            $"{(described ? "Described the feature" : "Kept the feature's description")} and wrote {Count(written.Stories.Count, "user story", "user stories")}{bugs} " +
            $"(the writer read {tools.Reads} files).");
    }

    private async Task<WorkItemTemplate?> ProposedAsync(WorkItemType type, CancellationToken cancellationToken)
    {
        return WorkItemTemplate.Proposed(await templates.GetAllAsync(type, cancellationToken));
    }

    private static string Count(int count, string singular, string? plural = null)
    {
        return count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";
    }
}
