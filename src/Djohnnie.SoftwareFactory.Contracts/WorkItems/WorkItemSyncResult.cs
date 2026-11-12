namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <param name="Repositories">The repositories that were looked at.</param>
/// <param name="Items">The accepted work items that were looked at, including the ones that failed.</param>
/// <param name="Changed">The items whose GitHub issue differed from what was stored.</param>
/// <param name="Failures">The items that could not be synced; the others were.</param>
/// <param name="ProgressWarnings">The items that were synced without (all of) Copilot's progress, e.g. for a token without access to pull requests.</param>
public sealed record WorkItemSyncResult(int Repositories, int Items, int Changed, IReadOnlyList<WorkItemSyncFailure> Failures, int ProgressWarnings = 0);

/// <param name="IssueNumber"><c>null</c> when the items of the repository could not be listed at all.</param>
public sealed record WorkItemSyncFailure(string RepositoryFullName, int? IssueNumber, string Error);
