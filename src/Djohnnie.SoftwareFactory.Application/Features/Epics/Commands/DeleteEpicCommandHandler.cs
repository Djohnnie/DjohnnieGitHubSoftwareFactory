using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class DeleteEpicCommandHandler(IEpicStore store) : IRequestHandler<DeleteEpicCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteEpicCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
        return new(new());
    }
}
