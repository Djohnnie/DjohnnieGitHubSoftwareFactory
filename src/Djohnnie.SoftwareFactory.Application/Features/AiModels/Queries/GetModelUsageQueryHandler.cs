using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class GetModelUsageQueryHandler(IAiModelStore store) : IRequestHandler<GetModelUsageQuery, Response<PagedResult<AiModelUsage>>>
{
    public async Task<Response<PagedResult<AiModelUsage>>> Handle(GetModelUsageQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetUsagePageAsync(request.Dto, cancellationToken));
    }
}
