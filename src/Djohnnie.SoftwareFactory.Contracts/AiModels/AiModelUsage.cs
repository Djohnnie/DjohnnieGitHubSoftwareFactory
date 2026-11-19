using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts.AiModels;

/// <summary>The running totals of what DGHSF agent worker sessions spent on one GitHub Copilot model.</summary>
/// <param name="Sessions">The agent sessions (run attempts) that used the model.</param>
/// <param name="Requests">The model calls in those sessions.</param>
public sealed record AiModelUsageTotals(
    int Sessions,
    long NanoAiu,
    int Requests,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long ReasoningTokens)
{
    public static AiModelUsageTotals None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The total cost in GitHub AI credits (1 credit = 0.01 USD).</summary>
    public decimal AiCredits
    {
        get
        {
            return NanoAiu / AgentRun.NanoAiuPerCredit;
        }
    }
}

/// <summary>
/// What one model cost in one session of the DGHSF agent worker: one attempt of an <see cref="AgentRun"/>. The model is
/// kept by name as the SDK reported it; <see cref="AiModelId"/> links it to the catalog entry it was counted on, if any.
/// </summary>
public sealed record AiModelUsage
{
    public Guid Id { get; init; }
    public Guid AgentRunId { get; init; }
    public Guid? AiModelId { get; init; }

    /// <summary>The run's attempt this session was, starting at 1.</summary>
    public int Attempt { get; init; }

    /// <summary>The repository as <c>owner/name</c>, in lowercase.</summary>
    public string RepositoryFullName { get; init; } = string.Empty;

    public Guid WorkItemId { get; init; }

    /// <summary>The work item of the run, or <c>null</c> when it was deleted.</summary>
    public WorkItemReference? WorkItem { get; init; }

    public string Model { get; init; } = string.Empty;
    public long NanoAiu { get; init; }

    /// <summary>Whether GitHub reported the cost of every call; when not, <see cref="NanoAiu"/> is a lower bound.</summary>
    public bool Complete { get; init; }

    public int Requests { get; init; }
    public decimal PremiumRequests { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long ReasoningTokens { get; init; }
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>The cost in GitHub AI credits (1 credit = 0.01 USD).</summary>
    public decimal AiCredits
    {
        get
        {
            return NanoAiu / AgentRun.NanoAiuPerCredit;
        }
    }
}

/// <summary>A page of the agent sessions that used a model, newest first.</summary>
/// <param name="Page">Zero-based.</param>
public sealed record AiModelUsageQuery(Guid ModelId, int Page, int PageSize)
{
    public const int MaxPageSize = 100;
}
