using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class GetModelSessionCountQueryHandler(IAiModelStore store) : IRequestHandler<GetModelSessionCountQuery, Response<AiModelSessionCount>>
{
    public async Task<Response<AiModelSessionCount>> Handle(GetModelSessionCountQuery request, CancellationToken cancellationToken)
    {
        return new(new AiModelSessionCount(await store.CountUsageSessionsAsync(cancellationToken)));
    }
}
