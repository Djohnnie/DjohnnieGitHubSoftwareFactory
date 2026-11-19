using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class GetArchivedRepositoriesQueryHandler(IRepositoryStore store) : IRequestHandler<GetArchivedRepositoriesQuery, Response<CollectionResult<RepositorySummary>>>
{
    public async Task<Response<CollectionResult<RepositorySummary>>> Handle(GetArchivedRepositoriesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetArchivedAsync(cancellationToken)));
    }
}
