using Azure.Messaging.ServiceBus;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Tasks;

/// <summary>
/// Puts the DGHSF agent worker's pending runs on their Service Bus queue, where the worker's Container Apps job picks them
/// up. Optional: without <c>AgentRuns__QueueName</c> it does nothing, so the app runs without the worker. It sends with the
/// Service Bus SDK instead of an output binding, because a binding would need the queue name even when it isn't used.
/// </summary>
public sealed class AgentRunFunctions(AgentRunDispatcher dispatcher, AgentRunQueueSender sender, ILogger<AgentRunFunctions> logger)
{
    /// <summary>On the same schedule as the background tasks (<c>TodoTaskDispatchSchedule</c>).</summary>
    [Function("AgentRunDispatch")]
    public async Task DispatchAsync([TimerTrigger("%TodoTaskDispatchSchedule%")] TimerInfo timer, CancellationToken cancellationToken)
    {
        if (!sender.IsConfigured)
        {
            return;
        }

        var ids = await dispatcher.DispatchAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return;
        }
        // A run whose message isn't sent stays queued and is queued again after AgentRuns__QueuedTimeout.
        await sender.SendAsync(ids, cancellationToken);
        logger.LogInformation("Sent {Count} agent runs to the queue.", ids.Count);
    }
}

/// <summary>Sends run ids to the agent run queue; not configured without a queue name or a Service Bus connection.</summary>
public sealed class AgentRunQueueSender : IAsyncDisposable
{
    private readonly ServiceBusClient? _client;
    private readonly ServiceBusSender? _sender;

    public AgentRunQueueSender(IConfiguration configuration, IOptions<AgentRunOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.QueueName))
        {
            return;
        }
        _client = AgentRunQueue.CreateClient(configuration);
        _sender = _client?.CreateSender(options.Value.QueueName);
    }

    public bool IsConfigured
    {
        get
        {
            return _sender is not null;
        }
    }

    public async Task SendAsync(IReadOnlyList<Guid> runIds, CancellationToken cancellationToken)
    {
        if (_sender is null)
        {
            return;
        }
        await _sender.SendMessagesAsync(runIds.Select(AgentRunQueue.Message), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender is not null)
        {
            await _sender.DisposeAsync();
        }
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
