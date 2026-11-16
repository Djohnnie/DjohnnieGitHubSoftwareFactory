using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GetWorkItemAncestorsQueryHandler(IWorkItemStore store) : IRequestHandler<GetWorkItemAncestorsQuery, Response<CollectionResult<WorkItem>>>
{
    public async Task<Response<CollectionResult<WorkItem>>> Handle(GetWorkItemAncestorsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAncestorsAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken)));
    }
}
