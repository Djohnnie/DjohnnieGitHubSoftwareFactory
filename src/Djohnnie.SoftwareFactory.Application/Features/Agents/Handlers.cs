using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

/// <summary>Loads the agents from GitHub, syncs the local rows, and lists favorites first.</summary>
public sealed class CustomAgentsHandler(GitHubApiClient gitHub, AgentStore store) :
    IRequestHandler<GetCustomAgentsQuery, Response<CollectionResult<AgentSummary>>>
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

/// <summary>Local agent data only; does not call GitHub.</summary>
public sealed class AgentHandlers(AgentStore store) :
    IRequestHandler<SyncAgentsCommand, Response<CollectionResult<string>>>,
    IRequestHandler<SetAgentFavoriteCommand, Response<OperationResult>>
{
    public async Task<Response<CollectionResult<string>>> Handle(SyncAgentsCommand request, CancellationToken cancellationToken) =>
        new(new((await store.SyncAsync(request.Dto.RepositoryFullName, request.Dto.FileNames, cancellationToken)).ToList()));

    public async Task<Response<OperationResult>> Handle(SetAgentFavoriteCommand request, CancellationToken cancellationToken)
    {
        await store.SetFavoriteAsync(request.Dto.RepositoryFullName, request.Dto.FileName, request.Dto.IsFavorite, cancellationToken);
        return new(new());
    }
}
