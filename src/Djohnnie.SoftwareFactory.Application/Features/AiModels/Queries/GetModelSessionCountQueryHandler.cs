using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class GetModelSessionCountQueryHandler(IAiModelStore store) : IRequestHandler<GetModelSessionCountQuery, Response<int>>
{
    public async Task<Response<int>> Handle(GetModelSessionCountQuery request, CancellationToken cancellationToken)
    {
        return new(await store.CountUsageSessionsAsync(cancellationToken));
    }
}
