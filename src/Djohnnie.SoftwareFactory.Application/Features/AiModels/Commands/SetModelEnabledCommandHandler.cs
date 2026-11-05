using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class SetModelEnabledCommandHandler(IAiModelStore store) : IRequestHandler<SetModelEnabledCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(SetModelEnabledCommand request, CancellationToken cancellationToken)
    {
        await store.SetEnabledAsync(request.Dto.Id, request.Dto.Enabled, cancellationToken);
        return new(new());
    }
}
