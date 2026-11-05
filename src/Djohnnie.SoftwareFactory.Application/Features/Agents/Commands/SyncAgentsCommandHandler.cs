using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

/// <summary>Local agent data only; does not call GitHub.</summary>
public sealed class SyncAgentsCommandHandler(IAgentStore store) : IRequestHandler<SyncAgentsCommand, Response<CollectionResult<string>>>
{
    public async Task<Response<CollectionResult<string>>> Handle(SyncAgentsCommand request, CancellationToken cancellationToken)
    {
        return new(new((await store.SyncAsync(request.Dto.RepositoryFullName, request.Dto.FileNames, cancellationToken)).ToList()));
    }
}
