using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class DeleteWorkItemTemplateCommandHandler(IWorkItemTemplateStore store) : IRequestHandler<DeleteWorkItemTemplateCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteWorkItemTemplateCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }
}
