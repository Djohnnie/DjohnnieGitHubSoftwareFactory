using Djohnnie.SoftwareFactory.Contracts.Repositories;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

/// <summary>The repositories known to the app: recently accessed ones and favorites.</summary>
public sealed class RepositoryService(ISender mediator)
{
    /// <summary>Every repository in the database, whether or not it was opened recently.</summary>
    public async Task<IReadOnlyList<RepositorySummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetAllRepositoriesQuery(new()), cancellationToken)).Dto.Items;
    }

    public async Task<IReadOnlyList<RepositorySummary>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetRecentRepositoriesQuery(new()), cancellationToken)).Dto.Items;
    }

    public async Task<IReadOnlyList<RepositorySummary>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetFavoriteRepositoriesQuery(new()), cancellationToken)).Dto.Items;
    }

    public async Task RecordAccessAsync(string fullName, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new RecordRepositoryAccessCommand(new(fullName)), cancellationToken);
    }

    public async Task SetFavoriteAsync(string fullName, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new SetRepositoryFavoriteCommand(new(fullName, isFavorite)), cancellationToken);
    }
}
