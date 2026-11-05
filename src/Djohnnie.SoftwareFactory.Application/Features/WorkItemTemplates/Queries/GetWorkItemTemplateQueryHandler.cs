using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class GetWorkItemTemplateQueryHandler(IWorkItemTemplateStore store) : IRequestHandler<GetWorkItemTemplateQuery, Response<WorkItemTemplate?>>
{
    public async Task<Response<WorkItemTemplate?>> Handle(GetWorkItemTemplateQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.Id, cancellationToken));
    }
}
