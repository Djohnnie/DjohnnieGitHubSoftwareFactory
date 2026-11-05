using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IWorkItemStore
{
    /// <summary>The items of a repository, optionally only of <paramref name="types"/> and/or the children of <paramref name="parentId"/>.</summary>
    Task<List<WorkItem>> GetAllAsync(string repositoryFullName, IReadOnlyList<WorkItemType>? types = null, int? parentId = null, CancellationToken cancellationToken = default);

    Task<WorkItem?> GetAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates an item; enforces the hierarchy rules and what can still change after acceptance.</summary>
    Task<WorkItem> SaveAsync(WorkItem item, CancellationToken cancellationToken = default);

    /// <summary>Deletes an item that has no children.</summary>
    Task DeleteAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default);

    Task<WorkItem> MarkAcceptedAsync(string repositoryFullName, int id, long issueId, int issueNumber, string issueUrl, CancellationToken cancellationToken);

    Task<WorkItem> MarkAssignedAsync(string repositoryFullName, int id, CancellationToken cancellationToken);

    /// <summary>Remembers GitHub's issue id of an item that was accepted before the id was stored.</summary>
    Task SetIssueIdAsync(string repositoryFullName, int id, long issueId, CancellationToken cancellationToken);
}
