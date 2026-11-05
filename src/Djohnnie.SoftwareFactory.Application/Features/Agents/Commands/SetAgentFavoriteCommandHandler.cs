using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

/// <summary>Local agent data only; does not call GitHub.</summary>
public sealed class SetAgentFavoriteCommandHandler(IAgentStore store) : IRequestHandler<SetAgentFavoriteCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(SetAgentFavoriteCommand request, CancellationToken cancellationToken)
    {
        await store.SetFavoriteAsync(request.Dto.RepositoryFullName, request.Dto.FileName, request.Dto.IsFavorite, cancellationToken);
        return new(new());
    }
}
