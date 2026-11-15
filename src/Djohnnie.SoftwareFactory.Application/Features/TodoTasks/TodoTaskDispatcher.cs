using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>
/// The outbox side of the TODO tasks: takes back stuck tasks and claims the due pending ones. The Tasks function app
/// sends the returned ids to the Service Bus queue; a send that gets lost is recovered after
/// <see cref="TodoTaskOptions.QueuedTimeout"/>.
/// </summary>
public sealed class TodoTaskDispatcher(ITodoTaskStore store, IOptions<TodoTaskOptions> options, ILogger<TodoTaskDispatcher> logger)
{
    /// <summary>The ids of the tasks to put on the queue.</summary>
    public async Task<IReadOnlyList<Guid>> DispatchAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var recovered = await store.RecoverAsync(settings.QueuedTimeout, cancellationToken);
        var claimed = await store.ClaimDueAsync(settings.DispatchBatchSize, cancellationToken);
        if (recovered > 0 || claimed.Count > 0)
        {
            logger.LogInformation("Dispatched {Claimed} TODO tasks; {Recovered} stuck tasks were taken back or ended.", claimed.Count, recovered);
        }
        return claimed;
    }
}
