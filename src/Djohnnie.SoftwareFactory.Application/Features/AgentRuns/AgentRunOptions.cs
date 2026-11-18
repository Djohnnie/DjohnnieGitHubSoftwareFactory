namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>How the DGHSF agent worker's runs are dispatched and retried (section <c>AgentRuns</c>).</summary>
public sealed class AgentRunOptions
{
    public const string SectionName = "AgentRuns";

    /// <summary>
    /// The Service Bus queue the dispatcher sends run ids to and the worker's Container Apps job scales on. Without it, the
    /// Tasks function app doesn't dispatch runs.
    /// </summary>
    public string? QueueName { get; set; }

    /// <summary>
    /// How long a worker owns a started run without renewing it. The worker renews it every third of this while the agent
    /// works, so it only decides how fast a crashed worker's run is taken back.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a run may stay queued before the dispatcher assumes its message was lost and queues it again. Keep it above
    /// the time the Container Apps job needs to start an execution.
    /// </summary>
    public TimeSpan QueuedTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many times a run is started before it fails for good. Only a run whose worker stopped, or that failed before the
    /// agent started (e.g. the clone), is retried: a retry after the agent ran would pay for the agent again.
    /// </summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>How many runs one dispatcher run queues at most.</summary>
    public int DispatchBatchSize { get; set; } = 20;
}
