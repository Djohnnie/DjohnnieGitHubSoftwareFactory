using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.SyncLog;

public sealed class GetSyncLogRepositoriesQueryHandler(ISyncLogStore store) : IRequestHandler<GetSyncLogRepositoriesQuery, Response<CollectionResult<string>>>
{
    public async Task<Response<CollectionResult<string>>> Handle(GetSyncLogRepositoriesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetRepositoriesAsync(cancellationToken)));
    }
}
