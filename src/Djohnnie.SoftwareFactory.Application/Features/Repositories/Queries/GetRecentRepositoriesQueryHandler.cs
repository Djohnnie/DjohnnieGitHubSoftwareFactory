using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class GetRecentRepositoriesQueryHandler(IRepositoryStore store) : IRequestHandler<GetRecentRepositoriesQuery, Response<CollectionResult<RepositorySummary>>>
{
    public async Task<Response<CollectionResult<RepositorySummary>>> Handle(GetRecentRepositoriesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetRecentAsync(cancellationToken)));
    }
}
