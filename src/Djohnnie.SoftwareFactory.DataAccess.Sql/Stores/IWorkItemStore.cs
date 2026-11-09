using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IWorkItemStore
{
    /// <summary>The items of a repository, optionally only of <paramref name="types"/> and/or the children of <paramref name="parentId"/>.</summary>
    Task<List<WorkItem>> GetAllAsync(string repositoryFullName, IReadOnlyList<WorkItemType>? types = null, Guid? parentId = null, CancellationToken cancellationToken = default);

    Task<WorkItem?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);

    /// <summary>The counts of a repository for every work item type, also the types without items.</summary>
    Task<List<WorkItemCounts>> GetCountsAsync(string repositoryFullName, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates an item; enforces the hierarchy rules and what can still change after acceptance.</summary>
    Task<WorkItem> SaveAsync(WorkItem item, CancellationToken cancellationToken = default);

    /// <summary>Creates drafts of the type the parent is split into, all or none.</summary>
    Task<List<WorkItem>> AddGeneratedChildrenAsync(string repositoryFullName, Guid parentId, IReadOnlyList<GeneratedWorkItem> items, CancellationToken cancellationToken = default);

    /// <summary>Deletes an item that has no children.</summary>
    Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a draft for acceptance, so concurrent accepts can't both create a GitHub issue. Refuses when another
    /// acceptance started less than <paramref name="claimTimeout"/> ago.
    /// </summary>
    /// <returns>
    /// When an earlier attempt that didn't finish started, so its issue may already exist on GitHub (it was created or
    /// updated since then); <c>null</c> when no such attempt exists.
    /// </returns>
    Task<DateTimeOffset?> BeginAcceptAsync(string repositoryFullName, Guid id, TimeSpan claimTimeout, CancellationToken cancellationToken);

    /// <summary>Ends a failed acceptance attempt; the item stays a draft.</summary>
    /// <param name="issueMayExist">Whether the issue may have been created anyway, so the next attempt looks for it first.</param>
    Task EndFailedAcceptAsync(string repositoryFullName, Guid id, bool issueMayExist, CancellationToken cancellationToken);

    /// <summary>Completes an acceptance claimed with <see cref="BeginAcceptAsync"/>.</summary>
    Task<WorkItem> MarkAcceptedAsync(string repositoryFullName, Guid id, long issueId, int issueNumber, string issueUrl, CancellationToken cancellationToken);

    Task<WorkItem> MarkAssignedAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the current state of the item's GitHub issue: its title, description and ids, and the status that follows
    /// from it (closed is done; open is in progress when Copilot is assigned, otherwise accepted).
    /// <see cref="WorkItem.UpdatedAt"/> only changes when something differs.
    /// </summary>
    Task<WorkItem> ApplyIssueAsync(string repositoryFullName, Guid id, WorkItemIssueSnapshot issue, CancellationToken cancellationToken);

    /// <summary>Remembers GitHub's issue id of an item that was accepted before the id was stored.</summary>
    Task SetIssueIdAsync(string repositoryFullName, Guid id, long issueId, CancellationToken cancellationToken);
}
