namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <param name="Repositories">The repositories that were looked at.</param>
/// <param name="Items">The accepted work items that were looked at, including the ones that failed.</param>
/// <param name="Changed">The items whose GitHub issue differed from what was stored.</param>
/// <param name="Failures">The items that could not be synced; the others were.</param>
public sealed record WorkItemSyncResult(int Repositories, int Items, int Changed, IReadOnlyList<WorkItemSyncFailure> Failures);

/// <param name="IssueNumber"><c>null</c> when the items of the repository could not be listed at all.</param>
public sealed record WorkItemSyncFailure(string RepositoryFullName, int? IssueNumber, string Error);
