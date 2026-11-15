using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>
/// Executes one queued background task (the Tasks function app calls it for every Service Bus message). Retries go through
/// the database, never through message redelivery: a message for a task that isn't queued (a duplicate, or a task that
/// was cancelled) is ignored.
/// </summary>
public sealed class TodoTaskExecutor(
    ITodoTaskStore store,
    IEnumerable<ITodoTaskHandler> handlers,
    IOptions<TodoTaskOptions> options,
    TimeProvider timeProvider,
    ILogger<TodoTaskExecutor> logger)
{
    /// <summary>Runs the task; returns its new status, or <c>null</c> when it wasn't started (not queued).</summary>
    public async Task<TodoTaskStatus?> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var task = await store.TryStartAsync(id, settings.Lease, cancellationToken);
        if (task is null)
        {
            logger.LogInformation("Background task {Id} is not queued (a duplicate message, or cancelled); ignored.", id);
            return null;
        }

        var handler = handlers.FirstOrDefault(h => h.Kind == task.Kind);
        if (handler is null)
        {
            return await store.FailAsync(id, $"There is no handler for {task.Kind} tasks.", null, CancellationToken.None);
        }

        logger.LogInformation("Starting background task {Id} ({Kind}, attempt {Attempt} of {MaxAttempts}) for {Repository}.",
            id, task.Kind, task.Attempts, task.MaxAttempts, task.RepositoryFullName);
        try
        {
            var outcome = await handler.ExecuteAsync(task, cancellationToken);
            if (await store.CompleteAsync(id, outcome.Result, outcome.SubTasks, CancellationToken.None))
            {
                logger.LogInformation("Background task {Id} done with {SubTasks} sub-tasks: {Result}", id, outcome.SubTasks.Count, outcome.Result);
                return outcome.SubTasks.Count > 0 ? TodoTaskStatus.Waiting : TodoTaskStatus.Succeeded;
            }

            logger.LogInformation("Background task {Id} was cancelled or taken back while it ran; its result is discarded.", id);
            return await store.GetStatusAsync(id, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is shutting down: hand the task back right away instead of waiting for its lease to end.
            await store.FailAsync(id, "The executor was stopped before the task finished.", timeProvider.GetUtcNow(), CancellationToken.None);
            throw;
        }
        catch (DomainRuleException ex)
        {
            logger.LogWarning("Background task {Id} failed: {Message}", id, ex.Message);
            return await store.FailAsync(id, ex.Message, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            var retryAt = timeProvider.GetUtcNow() + RetryDelay(settings, task.Attempts);
            logger.LogWarning(ex, "Background task {Id} failed (attempt {Attempt} of {MaxAttempts}).", id, task.Attempts, task.MaxAttempts);
            return await store.FailAsync(id, ex.Message, retryAt, CancellationToken.None);
        }
    }

    /// <summary>The wait before the next attempt: <see cref="TodoTaskOptions.RetryDelay"/>, doubled for every attempt so far.</summary>
    public static TimeSpan RetryDelay(TodoTaskOptions options, int attempts)
    {
        return options.RetryDelay * Math.Pow(2, Math.Clamp(attempts - 1, 0, 10));
    }
}
