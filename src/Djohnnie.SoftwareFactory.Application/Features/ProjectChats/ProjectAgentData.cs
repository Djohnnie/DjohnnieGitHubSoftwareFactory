using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;
using Djohnnie.SoftwareFactory.Application.Features.Mindmaps;
using Djohnnie.SoftwareFactory.Application.Features.Skills;
using Djohnnie.SoftwareFactory.Application.Features.SyncLog;
using Djohnnie.SoftwareFactory.Application.Features.TodoTasks;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

/// <summary>
/// What the DGHSF Agent's tools read, through the same application services the pages use, so every read goes through
/// the validation pipeline and is scoped to the repository.
/// </summary>
internal sealed class ProjectAgentData(
    string repositoryFullName,
    WorkItemService workItems,
    MindmapService mindmaps,
    KnowledgeBaseService knowledgeBase,
    WorkItemTemplateService templates,
    SkillService skills,
    TodoTaskService todoTasks,
    SyncLogService syncLog,
    GitHubService gitHub) : AgentInfrastructure.IProjectAgentData
{
    public string RepositoryFullName
    {
        get
        {
            return repositoryFullName;
        }
    }

    public bool CanSearchKnowledgeBase
    {
        get
        {
            return knowledgeBase.IsSearchEnabled;
        }
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(CancellationToken cancellationToken)
    {
        return await workItems.GetAllAsync(repositoryFullName, cancellationToken: cancellationToken);
    }

    public async Task<WorkItem?> GetWorkItemAsync(Guid id, CancellationToken cancellationToken)
    {
        return await workItems.GetAsync(repositoryFullName, id, cancellationToken);
    }

    public async Task<IReadOnlyList<MindmapSummary>> GetMindmapsAsync(CancellationToken cancellationToken)
    {
        return await mindmaps.GetAllAsync(repositoryFullName, cancellationToken);
    }

    public async Task<Mindmap?> GetMindmapAsync(Guid id, CancellationToken cancellationToken)
    {
        return await mindmaps.GetAsync(repositoryFullName, id, cancellationToken);
    }

    public async Task<KnowledgeLibrary> GetKnowledgeLibraryAsync(CancellationToken cancellationToken)
    {
        return await knowledgeBase.GetLibraryAsync(repositoryFullName, cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchKnowledgeBaseAsync(string query, CancellationToken cancellationToken)
    {
        return await knowledgeBase.SearchAsync(repositoryFullName, query, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItemTemplate>> GetWorkItemTemplatesAsync(CancellationToken cancellationToken)
    {
        var all = new List<WorkItemTemplate>();
        foreach (var type in WorkItemTypeRules.All)
        {
            all.AddRange(await templates.GetAllAsync(type, cancellationToken));
        }

        return all;
    }

    public async Task<RepositorySkills> GetSkillsAsync(CancellationToken cancellationToken)
    {
        return await skills.GetRepositorySkillsAsync(repositoryFullName, cancellationToken);
    }

    public async Task<IReadOnlyList<TodoTaskRun>> GetBackgroundTaskRunsAsync(CancellationToken cancellationToken)
    {
        return await todoTasks.GetRunsAsync(repositoryFullName, cancellationToken);
    }

    public async Task<IReadOnlyList<SyncLogEvent>> GetSyncLogAsync(int count, CancellationToken cancellationToken)
    {
        var page = await syncLog.GetPageAsync(new SyncLogQuery(0, count, SyncLogLevel.Information, repositoryFullName), cancellationToken);
        return page.Items;
    }

    public async Task<IReadOnlyList<RepositoryEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        var items = await gitHub.GetDirectoryAsync(repositoryFullName, path, cancellationToken);
        return items.Select(i => new RepositoryEntry(i.Path, i.Type)).ToList();
    }

    public async Task<string?> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        return (await gitHub.GetFileAsync(repositoryFullName, path, cancellationToken))?.Content;
    }
}
