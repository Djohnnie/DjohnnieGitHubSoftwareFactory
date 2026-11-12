using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.SyncLog;

public sealed class GetSyncLogQueryHandler(ISyncLogStore store) : IRequestHandler<GetSyncLogQuery, Response<PagedResult<SyncLogEvent>>>
{
    public async Task<Response<PagedResult<SyncLogEvent>>> Handle(GetSyncLogQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetPageAsync(request.Dto, cancellationToken));
    }
}
