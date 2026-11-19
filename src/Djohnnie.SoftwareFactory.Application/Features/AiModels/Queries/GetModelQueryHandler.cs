using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class GetModelQueryHandler(IAiModelStore store) : IRequestHandler<GetModelQuery, Response<AiModel?>>
{
    public async Task<Response<AiModel?>> Handle(GetModelQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.Id, cancellationToken));
    }
}
