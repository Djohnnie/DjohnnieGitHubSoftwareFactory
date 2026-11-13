using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class DeleteMindmapCommandHandler(IMindmapStore store) : IRequestHandler<DeleteMindmapCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteMindmapCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
        return new(new());
    }
}
