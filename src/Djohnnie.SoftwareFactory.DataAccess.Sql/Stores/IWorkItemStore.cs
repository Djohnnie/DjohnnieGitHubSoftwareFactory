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

    /// <summary>
    /// Claims a draft for acceptance, so concurrent accepts can't both create a GitHub issue. Refuses when another
    /// acceptance started less than <paramref name="claimTimeout"/> ago.
    /// </summary>
    /// <returns>
    /// When an earlier attempt that didn't finish started, so its issue may already exist on GitHub (it was created or
    /// updated since then); <c>null</c> when no such attempt exists.
    /// </returns>
    Task<DateTimeOffset?> BeginAcceptAsync(string repositoryFullName, int id, TimeSpan claimTimeout, CancellationToken cancellationToken);

    /// <summary>Ends a failed acceptance attempt; the item stays a draft.</summary>
    /// <param name="issueMayExist">Whether the issue may have been created anyway, so the next attempt looks for it first.</param>
    Task EndFailedAcceptAsync(string repositoryFullName, int id, bool issueMayExist, CancellationToken cancellationToken);

    /// <summary>Completes an acceptance claimed with <see cref="BeginAcceptAsync"/>.</summary>
    Task<WorkItem> MarkAcceptedAsync(string repositoryFullName, int id, long issueId, int issueNumber, string issueUrl, CancellationToken cancellationToken);

    Task<WorkItem> MarkAssignedAsync(string repositoryFullName, int id, CancellationToken cancellationToken);

    /// <summary>Remembers GitHub's issue id of an item that was accepted before the id was stored.</summary>
    Task SetIssueIdAsync(string repositoryFullName, int id, long issueId, CancellationToken cancellationToken);
}
