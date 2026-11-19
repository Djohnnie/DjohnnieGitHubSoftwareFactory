using Djohnnie.SoftwareFactory.Contracts.Repositories;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

/// <summary>The repositories known to the app: recently accessed ones, favorites and the ones removed from GitHub.</summary>
public sealed class RepositoryService(ISender mediator)
{
    /// <summary>Every repository in the database, whether or not it was opened recently or still exists on GitHub.</summary>
    public async Task<IReadOnlyList<RepositorySummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetAllRepositoriesQuery(new()), cancellationToken)).Dto.Items;
    }

    /// <summary>The repository as the database knows it, or <c>null</c> when it doesn't.</summary>
    public async Task<RepositorySummary?> FindAsync(string fullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new FindRepositoryQuery(new(fullName)), cancellationToken)).Dto;
    }

    /// <summary>The repositories the sync found removed from GitHub; they are not among the recent ones or the favorites.</summary>
    public async Task<IReadOnlyList<RepositorySummary>> GetArchivedAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetArchivedRepositoriesQuery(new()), cancellationToken)).Dto.Items;
    }

    /// <summary>
    /// Marks a known repository as removed from GitHub (which also takes it out of the favorites), or as back on GitHub.
    /// Returns whether anything changed.
    /// </summary>
    public async Task<bool> SetArchivedAsync(string fullName, bool isArchived, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SetRepositoryArchivedCommand(new(fullName, isArchived)), cancellationToken)).Dto.Changed;
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
