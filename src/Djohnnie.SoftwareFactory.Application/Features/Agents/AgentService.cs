using Djohnnie.SoftwareFactory.Contracts.Agents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

/// <summary>Local data about custom agents, such as favorites. The agent files themselves are managed through GitHub.</summary>
public sealed class AgentService(ISender mediator)
{
    /// <summary>Records the agents currently in the repository and returns the ids of the favorite ones.</summary>
    public async Task<IReadOnlySet<string>> SyncAsync(string repositoryFullName, IEnumerable<string> fileNames, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new SyncAgentsCommand(new(repositoryFullName, fileNames.ToList())), cancellationToken)).Dto.Items
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task SetFavoriteAsync(string repositoryFullName, string fileName, bool isFavorite, CancellationToken cancellationToken = default) =>
        await mediator.Send(new SetAgentFavoriteCommand(new(repositoryFullName, fileName, isFavorite)), cancellationToken);
}
