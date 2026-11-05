namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>The model the Model Selector picked for a work item, with the complexity it estimated.</summary>
/// <param name="ModelId">A managed model id.</param>
/// <param name="Complexity"><c>low</c>, <c>medium</c> or <c>high</c>.</param>
/// <param name="Reason">One short sentence.</param>
public sealed record ModelSelection(string ModelId, string Complexity, string Reason);
