namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>The work items of a repository, optionally only of some types and/or the children of one parent.</summary>
public sealed record WorkItemsRequest(string RepositoryFullName, IReadOnlyList<WorkItemType>? Types = null, int? ParentId = null);
