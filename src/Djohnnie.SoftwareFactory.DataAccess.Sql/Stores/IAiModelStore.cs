using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Common;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IAiModelStore
{
    Task<List<AiModel>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<AiModel>> GetEnabledAsync(CancellationToken cancellationToken = default);

    Task<AiModel?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The agent worker sessions that were counted on a model, newest first, each with its work item.</summary>
    Task<PagedResult<AiModelUsage>> GetUsagePageAsync(AiModelUsageQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The agent worker sessions that were counted on any model. A session that used several models counts once, which
    /// summing the running totals of the models would not.
    /// </summary>
    Task<int> CountUsageSessionsAsync(CancellationToken cancellationToken = default);

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
