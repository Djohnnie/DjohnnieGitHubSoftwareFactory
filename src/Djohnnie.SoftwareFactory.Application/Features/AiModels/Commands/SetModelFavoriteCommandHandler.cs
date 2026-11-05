using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class SetModelFavoriteCommandHandler(IAiModelStore store) : IRequestHandler<SetModelFavoriteCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(SetModelFavoriteCommand request, CancellationToken cancellationToken)
    {
        await store.SetFavoriteAsync(request.Dto.Id, request.Dto.IsFavorite, cancellationToken);
        return new(new());
    }
}
