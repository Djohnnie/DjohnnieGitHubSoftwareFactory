using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class DeleteWorkItemCommandHandler(IWorkItemStore store) : IRequestHandler<DeleteWorkItemCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteWorkItemCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
        return new(new());
    }
}
