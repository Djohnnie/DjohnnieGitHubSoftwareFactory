using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class GetEpicTemplatesQueryHandler(IEpicTemplateStore store) : IRequestHandler<GetEpicTemplatesQuery, Response<CollectionResult<EpicTemplate>>>
{
    public async Task<Response<CollectionResult<EpicTemplate>>> Handle(GetEpicTemplatesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(cancellationToken)));
    }
}
