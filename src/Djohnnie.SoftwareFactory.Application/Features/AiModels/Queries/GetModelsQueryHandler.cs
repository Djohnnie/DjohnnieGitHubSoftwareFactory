using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class GetModelsQueryHandler(IAiModelStore store) : IRequestHandler<GetModelsQuery, Response<CollectionResult<AiModel>>>
{
    public async Task<Response<CollectionResult<AiModel>>> Handle(GetModelsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(cancellationToken)));
    }
}
