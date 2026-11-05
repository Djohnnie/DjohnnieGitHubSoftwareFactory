using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class SaveWorkItemCommandHandler(IWorkItemStore store) : IRequestHandler<SaveWorkItemCommand, Response<WorkItem>>
{
    public async Task<Response<WorkItem>> Handle(SaveWorkItemCommand request, CancellationToken cancellationToken)
    {
        return new(await store.SaveAsync(request.Dto, cancellationToken));
    }
}
