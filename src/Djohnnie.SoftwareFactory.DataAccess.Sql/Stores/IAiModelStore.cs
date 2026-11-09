using Djohnnie.SoftwareFactory.Contracts.AiModels;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IAiModelStore
{
    Task<List<AiModel>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<AiModel>> GetEnabledAsync(CancellationToken cancellationToken = default);

    Task<AiModel> SaveAsync(AiModel model, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds new models and refreshes pricing details of existing ones (matched on name and tier).
    /// The model id and enabled state of existing models are kept, because those can be edited by the user.
    /// </summary>
    Task<ModelImportResult> ImportAsync(IEnumerable<AiModel> scraped, CancellationToken cancellationToken = default);
}
