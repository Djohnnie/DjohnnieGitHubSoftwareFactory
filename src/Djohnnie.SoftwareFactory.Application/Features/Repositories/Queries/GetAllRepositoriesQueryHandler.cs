using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class GetAllRepositoriesQueryHandler(IRepositoryStore store) : IRequestHandler<GetAllRepositoriesQuery, Response<CollectionResult<RepositorySummary>>>
{
    public async Task<Response<CollectionResult<RepositorySummary>>> Handle(GetAllRepositoriesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(cancellationToken)));
    }
}
