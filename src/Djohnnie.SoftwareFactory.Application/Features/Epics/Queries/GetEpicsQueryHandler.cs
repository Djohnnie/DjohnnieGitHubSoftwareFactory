using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class GetEpicsQueryHandler(IEpicStore store) : IRequestHandler<GetEpicsQuery, Response<CollectionResult<Epic>>>
{
    public async Task<Response<CollectionResult<Epic>>> Handle(GetEpicsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(request.Dto.RepositoryFullName, cancellationToken)));
    }
}
