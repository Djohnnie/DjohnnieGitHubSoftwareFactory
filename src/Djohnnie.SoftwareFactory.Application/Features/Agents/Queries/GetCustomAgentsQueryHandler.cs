using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

/// <summary>Loads the agents from GitHub, syncs the local rows, and lists favorites first.</summary>
public sealed class GetCustomAgentsQueryHandler(GitHubApiClient gitHub, IAgentStore store) : IRequestHandler<GetCustomAgentsQuery, Response<CollectionResult<AgentSummary>>>
{
    public async Task<Response<CollectionResult<AgentSummary>>> Handle(GetCustomAgentsQuery request, CancellationToken cancellationToken)
    {
        var agents = await AgentCatalog.LoadAsync(gitHub, request.Dto.RepositoryFullName, cancellationToken);
        var favorites = await store.SyncAsync(request.Dto.RepositoryFullName, agents.Select(a => a.Id), cancellationToken);
        return new(new(agents
            .Select(a => a with { IsFavorite = favorites.Contains(a.Id) })
            .OrderBy(a => a.IsFavorite ? 0 : 1)
            .ToList()));
    }
}
