namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <param name="WorkItem">The item after accepting or assigning it.</param>
/// <param name="AssignmentError">Why assigning the Copilot cloud agent failed, if it did.</param>
/// <param name="HierarchyError">Why linking the issue to its parent or children as sub-issues failed, if it did.</param>
public sealed record WorkItemAcceptResult(WorkItem WorkItem, string? AssignmentError, string? HierarchyError = null);
