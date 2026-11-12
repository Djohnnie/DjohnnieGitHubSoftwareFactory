using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.SyncLog;

/// <summary>Reads the events the work item sync function wrote; only the sync writes them.</summary>
public sealed class SyncLogService(IMediator mediator)
{
    public async Task<PagedResult<SyncLogEvent>> GetPageAsync(SyncLogQuery query, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetSyncLogQuery(query), cancellationToken)).Dto;
    }

    /// <summary>The repositories that have events, to filter on.</summary>
    public async Task<IReadOnlyList<string>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetSyncLogRepositoriesQuery(new()), cancellationToken)).Dto.Items;
    }
}
