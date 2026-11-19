using Djohnnie.SoftwareFactory.Contracts.Repositories;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IRepositoryStore
{
    /// <summary>Every repository in the database, including the ones removed from GitHub.</summary>
    Task<List<RepositorySummary>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The repository, or <c>null</c> when the database doesn't know it.</summary>
    Task<RepositorySummary?> FindAsync(string fullName, CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recently accessed repositories that are not favorites (those are listed separately) and still exist on GitHub.
    /// </summary>
    Task<List<RepositorySummary>> GetRecentAsync(CancellationToken cancellationToken = default);

    /// <summary>The favorite repositories that still exist on GitHub.</summary>
    Task<List<RepositorySummary>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    /// <summary>The repositories the sync found removed from GitHub (<see cref="RepositorySummary.IsArchived"/>).</summary>
    Task<List<RepositorySummary>> GetArchivedAsync(CancellationToken cancellationToken = default);

    Task RecordAccessAsync(string fullName, CancellationToken cancellationToken = default);

    /// <exception cref="Contracts.DomainRuleException">Making a repository that was removed from GitHub a favorite.</exception>
    Task SetFavoriteAsync(string fullName, bool isFavorite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a known repository as removed from GitHub, which also takes it out of the favorites, or as back on GitHub.
    /// Returns whether anything changed; an unknown repository is left alone.
    /// </summary>
    Task<bool> SetArchivedAsync(string fullName, bool isArchived, CancellationToken cancellationToken = default);
}
