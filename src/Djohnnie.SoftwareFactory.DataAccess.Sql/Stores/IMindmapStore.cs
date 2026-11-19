using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IMindmapStore
{
    /// <summary>The mindmaps of a repository, sorted by title.</summary>
    Task<List<MindmapSummary>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default);

    Task<Mindmap?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates (empty id) or replaces a mindmap with all its ideas.</summary>
    /// <exception cref="Contracts.DomainRuleException">The mindmap no longer exists in its repository.</exception>
    Task<Mindmap> SaveAsync(Mindmap value, CancellationToken cancellationToken = default);

    /// <summary>Links an idea to a work item (or unlinks it with <c>null</c>), changing nothing else of the map; <paramref name="markDone"/> also marks it done.</summary>
    /// <exception cref="Contracts.DomainRuleException">The mindmap or the idea no longer exists.</exception>
    Task<Mindmap> LinkAsync(string repositoryFullName, Guid mindmapId, Guid ideaId, Guid? workItemId, bool markDone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the planned work items as drafts and links each one to its ideas, all or none. An item's parent is another
    /// item of the plan or an existing work item.
    /// </summary>
    /// <param name="items">The items, parents before their children.</param>
    /// <exception cref="Contracts.DomainRuleException">The map, an idea or a parent no longer exists, an idea is linked already, or a type can't have its parent.</exception>
    Task<List<WorkItem>> AddPlannedWorkItemsAsync(string repositoryFullName, Guid mindmapId, IReadOnlyList<MindmapPlanItem> items, CancellationToken cancellationToken = default);

    /// <summary>The ideas of the repository's mindmaps that are linked to the work item, by mindmap title.</summary>
    Task<List<MindmapIdeaLink>> GetLinkedIdeasAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default);

    /// <summary>Marks every idea of the repository linked to the work item as done.</summary>
    /// <returns>The number of ideas that were not done yet.</returns>
    Task<int> MarkWorkItemDoneAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);
}
