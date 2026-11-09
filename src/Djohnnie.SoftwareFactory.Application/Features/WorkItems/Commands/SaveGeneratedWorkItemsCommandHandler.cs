using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class SaveGeneratedWorkItemsCommandHandler(IWorkItemStore store) : IRequestHandler<SaveGeneratedWorkItemsCommand, Response<CollectionResult<WorkItem>>>
{
    public async Task<Response<CollectionResult<WorkItem>>> Handle(SaveGeneratedWorkItemsCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        return new(new(await store.AddGeneratedChildrenAsync(dto.RepositoryFullName, dto.ParentId, dto.Items, cancellationToken)));
    }
}
