using Djohnnie.SoftwareFactory.Application.Features.TodoTasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Tasks;

/// <summary>
/// Runs the TODO tasks stored in SQL. The dispatcher (timer) moves due tasks to the Service Bus queue and takes back
/// stuck ones; the executor (queue trigger) runs one task per message. The database is the source of truth: a message
/// only carries a task id, and retries are scheduled in the database instead of through message redelivery.
/// </summary>
public sealed class TodoTaskFunctions(TodoTaskDispatcher dispatcher, TodoTaskExecutor executor, ILogger<TodoTaskFunctions> logger)
{
    /// <summary>Runs on the CRON expression in the <c>TodoTaskDispatchSchedule</c> app setting (six fields, with seconds, in UTC).</summary>
    [Function("TodoTaskDispatch")]
    [ServiceBusOutput("%TodoTaskQueueName%", Connection = "ServiceBus")]
    public async Task<string[]> DispatchAsync([TimerTrigger("%TodoTaskDispatchSchedule%")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var ids = await dispatcher.DispatchAsync(cancellationToken);
        return ids.Select(id => id.ToString()).ToArray();
    }

    /// <summary>Executes the task in the message. A message for a task that isn't queued (a duplicate) is completed without doing anything.</summary>
    [Function("TodoTaskExecute")]
    public async Task ExecuteAsync([ServiceBusTrigger("%TodoTaskQueueName%", Connection = "ServiceBus")] string message, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(message.Trim().Trim('"'), out var id))
        {
            logger.LogWarning("Ignored a message that is not a TODO task id: {Message}", message);
            return;
        }

        var status = await executor.ExecuteAsync(id, cancellationToken);
        logger.LogInformation("TODO task {Id} is now {Status}.", id, status?.ToString() ?? "unchanged");
    }
}
