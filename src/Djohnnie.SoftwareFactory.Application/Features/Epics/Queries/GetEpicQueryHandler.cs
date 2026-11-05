using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class GetEpicQueryHandler(IEpicStore store) : IRequestHandler<GetEpicQuery, Response<Epic?>>
{
    public async Task<Response<Epic?>> Handle(GetEpicQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
