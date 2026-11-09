using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GetWorkItemCountsQueryHandler(IWorkItemStore store) : IRequestHandler<GetWorkItemCountsQuery, Response<CollectionResult<WorkItemCounts>>>
{
    public async Task<Response<CollectionResult<WorkItemCounts>>> Handle(GetWorkItemCountsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetCountsAsync(request.Dto.RepositoryFullName, cancellationToken)));
    }
}
