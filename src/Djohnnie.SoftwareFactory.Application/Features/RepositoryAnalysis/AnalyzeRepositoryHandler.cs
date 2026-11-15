using System.Text;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Application.Features.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>
/// The first task of a "Generate from repository" run: the Repository Analyst plans epics and features, which are saved
/// as drafts, and every new feature gets a <see cref="TodoTaskKind.WriteFeatureWorkItems"/> sub-task.
/// </summary>
public sealed class AnalyzeRepositoryHandler(
    RepositoryFiles repositoryFiles,
    IWorkItemStore items,
    IWorkItemTemplateStore templates,
    ITodoTaskStore tasks,
    InternalAgentRegistry registry,
    BackgroundAgents agents) : ITodoTaskHandler
{
    /// <summary>The analyst gets a larger read budget than a writer: it needs an overview of the whole repository.</summary>
    public const int MaxReads = 40;

    public TodoTaskKind Kind
    {
        get
        {
            return TodoTaskKind.AnalyzeRepository;
        }
    }

    public async Task<TodoTaskOutcome> ExecuteAsync(TodoTask task, CancellationToken cancellationToken)
    {
        var payload = TodoTaskPayloads.Read<AnalyzeRepositoryPayload>(task);
        var agent = await RequireAgentAsync(registry, agents, InternalAgentKeys.RepositoryAnalyst, cancellationToken);
        var repository = task.RepositoryFullName;
        var snapshot = await repositoryFiles.ReadAsync(repository, payload.PathPrefix, task.RootId, cancellationToken);

        var existingEpics = await items.GetAllAsync(repository, [WorkItemType.Epic], cancellationToken: cancellationToken);
        var existingFeatures = await items.GetAllAsync(repository, [WorkItemType.Feature], cancellationToken: cancellationToken);
        var epicTemplate = WorkItemTemplate.Proposed(await templates.GetAllAsync(WorkItemType.Epic, cancellationToken));
        var tools = snapshot.CreateTools(MaxReads);
        var input = new RepositoryAnalysisInput(repository, payload.Mode, payload.PathPrefix, payload.MaxFeatures, snapshot.Outline,
            existingEpics, existingFeatures, epicTemplate);
        var plan = await agents.AnalyzeRepositoryAsync(agent, input, tools, cancellationToken);
        if (plan.Count == 0)
        {
            return TodoTaskOutcome.Done($"The Repository Analyst planned no new epics or features (it read {tools.Reads} files).");
        }

        // Don't save drafts for a run that was cancelled while the agent was thinking.
        if (await tasks.GetStatusAsync(task.Id, cancellationToken) != TodoTaskStatus.Running)
        {
            return TodoTaskOutcome.Done("Cancelled before the plan was saved.");
        }

        var allFeatures = plan.SelectMany(epic => epic.Features).Select(feature => feature.Title).ToList();
        var subTasks = new List<NewTodoTask>();
        int createdEpics = 0, reusedEpics = 0;
        foreach (var epic in plan)
        {
            var existing = existingEpics.FirstOrDefault(e => string.Equals(e.Title.Trim(), epic.Title, StringComparison.OrdinalIgnoreCase));
            WorkItem saved;
            if (existing is not null)
            {
                saved = existing;
                reusedEpics++;
            }
            else
            {
                saved = (await items.AddGeneratedAsync(repository, null, WorkItemType.Epic, [new GeneratedWorkItem(epic.Title, epic.Description)],
                    task.RootId, cancellationToken)).Single();
                createdEpics++;
            }

            var features = await items.AddGeneratedAsync(repository, saved.Id, WorkItemType.Feature,
                epic.Features.Select(feature => new GeneratedWorkItem(feature.Title, PlannedDescription(feature))).ToList(), task.RootId, cancellationToken);
            for (var i = 0; i < features.Count; i++)
            {
                var feature = epic.Features[i];
                var next = new WriteFeatureWorkItemsPayload(payload.Mode, payload.PathPrefix, saved.Id, features[i].Id, feature,
                    allFeatures.Where(title => !string.Equals(title, feature.Title, StringComparison.OrdinalIgnoreCase)).ToList());
                subTasks.Add(new NewTodoTask(TodoTaskKind.WriteFeatureWorkItems, $"Write the work items of '{feature.Title}'", TodoTaskPayloads.Write(next)));
            }
        }

        var result = new StringBuilder()
            .Append($"Planned {subTasks.Count} features in {createdEpics} new and {reusedEpics} existing epics ")
            .Append($"({snapshot.Files.Count} readable files; the analyst read {tools.Reads}).");
        if (snapshot.Truncated)
        {
            result.Append(" GitHub truncated the file tree of this large repository, so some files were unknown; analyze a folder for a complete picture.");
        }
        return new TodoTaskOutcome(result.ToString(), subTasks);
    }

    /// <summary>The feature's description until the writer replaces it: the analyst's summary and the paths it pointed out.</summary>
    public static string PlannedDescription(PlannedFeature feature)
    {
        var builder = new StringBuilder(feature.Summary);
        if (feature.Paths.Count > 0)
        {
            builder.AppendLine().AppendLine().AppendLine("**Relevant paths**").AppendLine();
            foreach (var path in feature.Paths)
            {
                builder.Append("- `").Append(path).AppendLine("`");
            }
        }
        return builder.ToString().Trim();
    }

    /// <exception cref="DomainRuleException">The agent is disabled or no model is configured; a retry won't help.</exception>
    internal static async Task<InternalAgent> RequireAgentAsync(InternalAgentRegistry registry, BackgroundAgents agents, string key, CancellationToken cancellationToken)
    {
        var agent = await registry.GetAsync(key, cancellationToken);
        if (!agents.IsConfigured || !agent.IsEnabled)
        {
            throw new DomainRuleException($"The {agent.Name} agent is disabled or not configured.");
        }
        return agent;
    }
}
