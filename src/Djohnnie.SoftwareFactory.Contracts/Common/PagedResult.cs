namespace Djohnnie.SoftwareFactory.Contracts.Common;

/// <summary>One page of a longer list.</summary>
/// <param name="TotalCount">The number of items on all pages together.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);
