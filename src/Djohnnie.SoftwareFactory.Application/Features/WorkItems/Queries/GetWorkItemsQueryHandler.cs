using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GetWorkItemsQueryHandler(IWorkItemStore store) : IRequestHandler<GetWorkItemsQuery, Response<CollectionResult<WorkItem>>>
{
    public async Task<Response<CollectionResult<WorkItem>>> Handle(GetWorkItemsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(request.Dto.RepositoryFullName, request.Dto.Types, request.Dto.ParentId, cancellationToken)));
    }
}
