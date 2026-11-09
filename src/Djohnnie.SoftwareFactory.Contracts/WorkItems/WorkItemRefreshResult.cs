namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <param name="WorkItem">The item as stored after refreshing it from its GitHub issue.</param>
/// <param name="Changed">Whether the issue differed from what was stored.</param>
public sealed record WorkItemRefreshResult(WorkItem WorkItem, bool Changed);
