using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>
/// The outbox side of the agent runs: takes back stuck runs and claims the pending ones. The Tasks function app sends the
/// returned ids to the Service Bus queue the worker's Container Apps job scales on; a send that gets lost is recovered after
/// <see cref="AgentRunOptions.QueuedTimeout"/>.
/// </summary>
public sealed class AgentRunDispatcher(IAgentRunStore store, IOptions<AgentRunOptions> options, ILogger<AgentRunDispatcher> logger)
{
    /// <summary>The ids of the runs to put on the queue.</summary>
    public async Task<IReadOnlyList<Guid>> DispatchAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var recovered = await store.RecoverAsync(settings.QueuedTimeout, cancellationToken);
        var claimed = await store.ClaimDueAsync(settings.DispatchBatchSize, cancellationToken);
        if (recovered > 0 || claimed.Count > 0)
        {
            logger.LogInformation("Dispatched {Claimed} agent runs; {Recovered} stuck runs were taken back or ended.", claimed.Count, recovered);
        }
        return claimed;
    }
}
