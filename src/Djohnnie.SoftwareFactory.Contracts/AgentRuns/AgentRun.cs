namespace Djohnnie.SoftwareFactory.Contracts.AgentRuns;

/// <summary>
/// One task of the DGHSF agent worker on a user story, bug or task: the worker clones the repository, lets the Copilot SDK
/// agent implement the item on a new branch, pushes it and opens a pull request that closes the item's issue. Every time the
/// worker starts the task it adds an <see cref="AgentRunAttempt"/> (a run), so retries are runs of the same task; the task
/// keeps the final status and outcome and the totals of its runs. The SQL row is the source of truth; the Service Bus queue
/// only carries its id.
/// </summary>
public sealed record AgentRun
{
    public const int MaxSummaryLength = 20_000;
    public const int MaxErrorLength = 4_000;

    /// <summary>Nano AI units in one GitHub AI credit, as the Copilot SDK reports usage (SI <c>nano</c> prefix).</summary>
    public const decimal NanoAiuPerCredit = 1_000_000_000m;

    public Guid Id { get; init; }
    public Guid WorkItemId { get; init; }

    /// <summary>The repository as <c>owner/name</c>, in lowercase.</summary>
    public string RepositoryFullName { get; init; } = string.Empty;

    public AgentRunStatus Status { get; init; }
    public int Attempts { get; init; }
    public int MaxAttempts { get; init; }

    /// <summary>The model the item asked for, or <c>null</c> for automatic model selection.</summary>
    public string? RequestedModel { get; init; }

    /// <summary>The models that answered, comma separated, as the SDK reported them.</summary>
    public string? Models { get; init; }

    /// <summary>The branch the worker pushed the agent's commit to.</summary>
    public string? Branch { get; init; }

    public int? PullRequestNumber { get; init; }
    public string? PullRequestUrl { get; init; }

    /// <summary>The agent's last message: what it did.</summary>
    public string? Summary { get; init; }

    /// <summary>Why a run failed, or for a run that succeeded, why the agent stopped early (its pull request is then a draft).</summary>
    public string? Error { get; init; }

    /// <summary>The run's cost in nano AI units, or <c>null</c> when the agent never started.</summary>
    public long? NanoAiu { get; init; }

    /// <summary>Whether GitHub reported the cost of every model call; when not, <see cref="NanoAiu"/> is a lower bound.</summary>
    public bool UsageComplete { get; init; }

    public decimal? PremiumRequests { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? QueuedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The run's cost in GitHub AI credits (1 credit = 0.01 USD).</summary>
    public decimal? AiCredits
    {
        get
        {
            return NanoAiu is { } nano ? nano / NanoAiuPerCredit : null;
        }
    }

    public bool IsActive
    {
        get
        {
            return !Status.IsFinal;
        }
    }

    /// <summary>How long the agent ran, for a run that started.</summary>
    public TimeSpan? Duration
    {
        get
        {
            return StartedAt is { } started && CompletedAt is { } completed ? completed - started : null;
        }
    }
}

/// <summary>
/// One run (attempt) of an <see cref="AgentRun"/> task: what the agent did, cost and ended with the time the worker started it.
/// </summary>
public sealed record AgentRunAttempt
{
    public Guid Id { get; init; }
    public Guid AgentRunId { get; init; }

    /// <summary>The number of this run within its task, starting at 1.</summary>
    public int Attempt { get; init; }

    public AgentRunStatus Status { get; init; }
    public string? Models { get; init; }
    public string? Branch { get; init; }
    public int? PullRequestNumber { get; init; }
    public string? PullRequestUrl { get; init; }
    public string? Summary { get; init; }

    /// <summary>Why the run failed, or for a run that succeeded, why the agent stopped early.</summary>
    public string? Error { get; init; }

    /// <summary>The run's cost in nano AI units, or <c>null</c> when the agent never started.</summary>
    public long? NanoAiu { get; init; }

    public bool UsageComplete { get; init; }
    public decimal? PremiumRequests { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The run's cost in GitHub AI credits (1 credit = 0.01 USD).</summary>
    public decimal? AiCredits
    {
        get
        {
            return NanoAiu is { } nano ? nano / AgentRun.NanoAiuPerCredit : null;
        }
    }

    /// <summary>How long the run took, for a run that ended.</summary>
    public TimeSpan? Duration
    {
        get
        {
            return CompletedAt is { } completed ? completed - StartedAt : null;
        }
    }
}

/// <summary>
/// A task with the work item it belongs to and its runs (newest first), for lists of tasks; <paramref name="WorkItem"/> is
/// <c>null</c> when the item was deleted.
/// </summary>
public sealed record AgentRunOverview(AgentRun Run, WorkItems.WorkItemReference? WorkItem, IReadOnlyList<AgentRunAttempt> Runs);

/// <summary>What one session of the agent cost, as the Copilot SDK reported it at the end.</summary>
/// <param name="PerModel">The same usage per model that answered, which is linked to the GitHub Copilot model catalog.</param>
public sealed record AgentRunUsage(
    long NanoAiu,
    bool Complete,
    decimal? PremiumRequests,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    string? Models,
    IReadOnlyList<AgentRunModelUsage>? PerModel = null)
{
    public static AgentRunUsage None { get; } = new(0, true, null, 0, 0, 0, 0, null);
}

/// <summary>What one model cost in one session of the agent, as the Copilot SDK reported it.</summary>
/// <param name="Model">The model id as the SDK reported it, e.g. <c>claude-sonnet-4.5</c>.</param>
/// <param name="Complete">Whether GitHub reported the cost of every call to this model; when not, <paramref name="NanoAiu"/> is a lower bound.</param>
/// <param name="ReasoningTokens">The part of <paramref name="OutputTokens"/> used for reasoning, for information.</param>
public sealed record AgentRunModelUsage(
    string Model,
    long NanoAiu,
    bool Complete,
    int Requests,
    decimal PremiumRequests,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long ReasoningTokens);

/// <summary>What a run that finished produced.</summary>
/// <param name="Warning">Why the agent stopped before it finished (e.g. the credit limit); its work is then a draft pull request.</param>
public sealed record AgentRunResult(string Branch, int PullRequestNumber, string PullRequestUrl, string? Summary, AgentRunUsage Usage, string? Warning = null);
