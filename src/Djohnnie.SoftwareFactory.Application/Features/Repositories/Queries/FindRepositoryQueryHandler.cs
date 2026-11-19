using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class FindRepositoryQueryHandler(IRepositoryStore store) : IRequestHandler<FindRepositoryQuery, Response<RepositorySummary?>>
{
    public async Task<Response<RepositorySummary?>> Handle(FindRepositoryQuery request, CancellationToken cancellationToken)
    {
        return new(await store.FindAsync(request.Dto.RepositoryFullName, cancellationToken));
    }
}
