using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

public sealed class DeleteInternalModelCommandHandler(IInternalModelStore store) : IRequestHandler<DeleteInternalModelCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteInternalModelCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }
}
