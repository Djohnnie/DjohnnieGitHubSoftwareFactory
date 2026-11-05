using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class GetEpicTemplateQueryHandler(IEpicTemplateStore store) : IRequestHandler<GetEpicTemplateQuery, Response<EpicTemplate?>>
{
    public async Task<Response<EpicTemplate?>> Handle(GetEpicTemplateQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.Id, cancellationToken));
    }
}
