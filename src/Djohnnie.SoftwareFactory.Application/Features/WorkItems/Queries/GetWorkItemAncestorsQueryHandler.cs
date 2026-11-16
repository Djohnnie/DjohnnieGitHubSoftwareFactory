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
        var ancestors = new List<WorkItem>();
        Guid? id = request.Dto.Id;
        // The hierarchy is at most epic > feature > story; the visited check only guards against a corrupt loop.
        while (id is { } current && ancestors.All(a => a.Id != current)
            && await store.GetAsync(request.Dto.RepositoryFullName, current, cancellationToken) is { } item)
        {
            ancestors.Add(item);
            id = item.ParentId;
        }
        return new(new(ancestors));
    }
}
