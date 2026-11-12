namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <param name="WorkItem">The item as stored after refreshing it from its GitHub issue.</param>
/// <param name="Changed">Whether the issue differed from what was stored.</param>
/// <param name="ProgressWarning">
/// Why Copilot's progress could not be read (completely, or only its agent run), e.g. a token without access to pull
/// requests or Actions; <c>null</c> when it could. The issue itself was refreshed either way.
/// </param>
public sealed record WorkItemRefreshResult(WorkItem WorkItem, bool Changed, string? ProgressWarning = null);
