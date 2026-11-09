using Djohnnie.SoftwareFactory.Contracts.AiModels;

namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>
/// One request an internal agent sent to its model. The model name, prices and costs are copies from that moment, so
/// changing or deleting the model later doesn't change what was spent.
/// </summary>
public sealed record InternalAgentUsage(
    DateTimeOffset OccurredAt,
    string AgentKey,
    string AgentName,
    InternalAgentUsageSource Source,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningTokens,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    decimal CachedInputPricePerMillion,
    decimal InputCost,
    decimal OutputCost)
{
    /// <summary>How many requests the agent's page shows.</summary>
    public const int RecentCount = 25;

    /// <summary>How many requests the overview of all agents shows.</summary>
    public const int RecentOverallCount = 50;

    public decimal TotalCost => InputCost + OutputCost;

    public static string FormatCost(decimal cost)
    {
        return AiModel.FormatPrice(cost);
    }
}
