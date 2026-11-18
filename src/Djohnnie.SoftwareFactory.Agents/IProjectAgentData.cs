using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// What DGHSF knows about one repository, read-only, for the DGHSF Agent's tools (<see cref="ProjectAgentTools"/>).
/// Every method is scoped to <see cref="RepositoryFullName"/>.
/// </summary>
public interface IProjectAgentData
{
    string RepositoryFullName { get; }

    /// <summary>Whether <see cref="SearchKnowledgeBaseAsync"/> can be used (Azure AI Search is configured).</summary>
    bool CanSearchKnowledgeBase { get; }

    Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(CancellationToken cancellationToken);

    /// <summary>The work item, or <c>null</c> when the repository has no item with that id.</summary>
    Task<WorkItem?> GetWorkItemAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MindmapSummary>> GetMindmapsAsync(CancellationToken cancellationToken);

    /// <summary>The mindmap, or <c>null</c> when the repository has no mindmap with that id.</summary>
    Task<Mindmap?> GetMindmapAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The global files and the repository's folders and files.</summary>
    Task<KnowledgeLibrary> GetKnowledgeLibraryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeSearchResult>> SearchKnowledgeBaseAsync(string query, CancellationToken cancellationToken);

    /// <summary>The templates of every work item type.</summary>
    Task<IReadOnlyList<WorkItemTemplate>> GetWorkItemTemplatesAsync(CancellationToken cancellationToken);

    /// <summary>The managed skills and whether each is enabled in the repository (reads GitHub).</summary>
    Task<RepositorySkills> GetSkillsAsync(CancellationToken cancellationToken);

    /// <summary>The repository's background task runs, newest first.</summary>
    Task<IReadOnlyList<TodoTaskRun>> GetBackgroundTaskRunsAsync(CancellationToken cancellationToken);

    /// <summary>The newest events of the work item sync for the repository, newest first.</summary>
    Task<IReadOnlyList<SyncLogEvent>> GetSyncLogAsync(int count, CancellationToken cancellationToken);

    /// <summary>Lists a directory of the repository (empty path = root) on the default branch.</summary>
    Task<IReadOnlyList<RepositoryEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken);

    /// <summary>Reads a file of the repository on the default branch, or <c>null</c> when it does not exist.</summary>
    Task<string?> ReadFileAsync(string path, CancellationToken cancellationToken);
}
