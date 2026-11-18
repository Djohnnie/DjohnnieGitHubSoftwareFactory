using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class DeleteProjectChatCommandHandler(IProjectChatStore store) : IRequestHandler<DeleteProjectChatCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteProjectChatCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
        return new(new());
    }
}
