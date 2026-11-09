namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>How far the items of one type got in a repository; each number includes the next.</summary>
/// <param name="Existing">All items, including drafts.</param>
/// <param name="Accepted">The items with a GitHub issue, whether they are accepted, in progress or done.</param>
/// <param name="Finished">The items that are done.</param>
public sealed record WorkItemCounts(WorkItemType Type, int Existing, int Accepted, int Finished);
