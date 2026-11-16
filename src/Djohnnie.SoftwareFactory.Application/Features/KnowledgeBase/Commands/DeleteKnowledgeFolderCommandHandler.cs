using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class DeleteKnowledgeFolderCommandHandler(IKnowledgeBaseStore store) : IRequestHandler<DeleteKnowledgeFolderCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteKnowledgeFolderCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteFolderAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
        return new(new());
    }
}
