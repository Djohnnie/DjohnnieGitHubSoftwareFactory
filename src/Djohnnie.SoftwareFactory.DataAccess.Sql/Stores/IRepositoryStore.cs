using Djohnnie.SoftwareFactory.Contracts.Repositories;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IRepositoryStore
{
    Task<List<RepositorySummary>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The most recently accessed repositories that are not favorites (those are listed separately).</summary>
    Task<List<RepositorySummary>> GetRecentAsync(CancellationToken cancellationToken = default);

    Task<List<RepositorySummary>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    Task RecordAccessAsync(string fullName, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(string fullName, bool isFavorite, CancellationToken cancellationToken = default);
}
