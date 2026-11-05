using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class DeleteModelCommandHandler(IAiModelStore store) : IRequestHandler<DeleteModelCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteModelCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }
}
