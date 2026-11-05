using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class DeleteTemplateCommandHandler(IAgentTemplateStore store) : IRequestHandler<DeleteTemplateCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteTemplateCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }
}
