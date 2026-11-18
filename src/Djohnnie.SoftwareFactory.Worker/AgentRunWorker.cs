using Azure.Messaging.ServiceBus;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>
/// One execution of the Container Apps job: takes one message from the queue, starts the run in it and executes it. The
/// message is completed as soon as the run is started: from then on the database owns the run (its lease), because an agent
/// works far longer than a Service Bus lock lasts.
/// </summary>
public sealed class AgentRunWorker(
    ServiceBusClient serviceBus,
    IAgentRunStore runs,
    AgentRunExecutor executor,
    IOptions<AgentRunOptions> runOptions,
    IOptions<AgentWorkerOptions> workerOptions,
    ILogger<AgentRunWorker> logger)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var queue = runOptions.Value.QueueName;
        if (string.IsNullOrWhiteSpace(queue))
        {
            throw new InvalidOperationException($"{AgentRunOptions.SectionName}:{nameof(AgentRunOptions.QueueName)} must be configured.");
        }

        await using var receiver = serviceBus.CreateReceiver(queue, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        var message = await receiver.ReceiveMessageAsync(workerOptions.Value.ReceiveTimeout, cancellationToken);
        if (message is null)
        {
            // Another execution took the message that started this one.
            logger.LogInformation("No agent run is waiting on {Queue}.", queue);
            return;
        }

        if (AgentRunQueue.RunId(message.Body.ToString()) is not { } id)
        {
            logger.LogWarning("Dead-lettered a message that is not an agent run id: {MessageId}", message.MessageId);
            await receiver.DeadLetterMessageAsync(message, "NotAnAgentRunId", cancellationToken: cancellationToken);
            return;
        }

        var run = await runs.TryStartAsync(id, runOptions.Value.Lease, cancellationToken);
        await receiver.CompleteMessageAsync(message, cancellationToken);
        if (run is null)
        {
            logger.LogInformation("Agent run {Id} isn't queued (a duplicate message, or cancelled); nothing to do.", id);
            return;
        }

        logger.LogInformation("Started agent run {Id} (attempt {Attempt} of {MaxAttempts}).", id, run.Attempts, run.MaxAttempts);
        var status = await executor.ExecuteAsync(run, cancellationToken);
        logger.LogInformation("Agent run {Id} is now {Status}.", id, status?.ToString() ?? "unchanged");
    }
}
