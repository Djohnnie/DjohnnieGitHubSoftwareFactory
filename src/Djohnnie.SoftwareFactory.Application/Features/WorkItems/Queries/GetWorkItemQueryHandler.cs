using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GetWorkItemQueryHandler(IWorkItemStore store) : IRequestHandler<GetWorkItemQuery, Response<WorkItem?>>
{
    public async Task<Response<WorkItem?>> Handle(GetWorkItemQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
