using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

public sealed record InternalAgentKeyRequest(string Key);
public sealed record SaveInternalAgentRequest(InternalAgent Agent);
public sealed record SelectWorkItemModelRequest(WorkItem WorkItem);
public sealed record TestInternalAgentModelRequest(string Model);

/// <param name="Error">Why the model could not be used, or <c>null</c> when it responded.</param>
public sealed record ModelTestResult(string? Error);

/// <param name="Selection">The selection, or <c>null</c> when the Model Selector is disabled, unconfigured or picked nothing usable.</param>
public sealed record ModelSelectionResult(ModelSelection? Selection);
