using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

public sealed class GetInternalModelsQueryHandler(IInternalModelStore store) : IRequestHandler<GetInternalModelsQuery, Response<CollectionResult<InternalModel>>>
{
    public async Task<Response<CollectionResult<InternalModel>>> Handle(GetInternalModelsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(cancellationToken)));
    }
}
