using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class GetWorkItemTemplatesQueryHandler(IWorkItemTemplateStore store) : IRequestHandler<GetWorkItemTemplatesQuery, Response<CollectionResult<WorkItemTemplate>>>
{
    public async Task<Response<CollectionResult<WorkItemTemplate>>> Handle(GetWorkItemTemplatesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(request.Dto.Type, cancellationToken)));
    }
}
