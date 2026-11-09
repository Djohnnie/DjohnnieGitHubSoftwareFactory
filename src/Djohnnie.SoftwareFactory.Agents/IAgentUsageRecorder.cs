using Djohnnie.SoftwareFactory.Contracts.InternalAgents;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>Keeps track of what the model requests of the internal agents cost.</summary>
public interface IAgentUsageRecorder
{
    /// <summary>Records one model request. Never throws: a failure to record must not break the agent's work.</summary>
    /// <param name="agent">The agent, as it was when the request's conversation or run started.</param>
    /// <param name="usage">The tokens of the request.</param>
    /// <param name="source">What caused the request.</param>
    Task RecordAsync(InternalAgent agent, ModelUsage usage, InternalAgentUsageSource source);
}
