namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>The running totals of one internal agent, or of several together.</summary>
public sealed record InternalAgentUsageTotals(
    long RequestCount,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningTokens,
    decimal InputCost,
    decimal OutputCost)
{
    public decimal TotalCost => InputCost + OutputCost;

    public static InternalAgentUsageTotals Sum(IReadOnlyCollection<InternalAgent> agents)
    {
        return new(
            agents.Sum(a => a.RequestCount),
            agents.Sum(a => a.TotalInputTokens),
            agents.Sum(a => a.TotalOutputTokens),
            agents.Sum(a => a.TotalCachedInputTokens),
            agents.Sum(a => a.TotalReasoningTokens),
            agents.Sum(a => a.TotalInputCost),
            agents.Sum(a => a.TotalOutputCost));
    }
}
