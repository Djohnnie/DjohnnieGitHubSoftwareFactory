using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class SetRepositoryFavoriteCommandHandler(IRepositoryStore store) : IRequestHandler<SetRepositoryFavoriteCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(SetRepositoryFavoriteCommand request, CancellationToken cancellationToken)
    {
        await store.SetFavoriteAsync(request.Dto.RepositoryFullName, request.Dto.IsFavorite, cancellationToken);
        return new(new());
    }
}
