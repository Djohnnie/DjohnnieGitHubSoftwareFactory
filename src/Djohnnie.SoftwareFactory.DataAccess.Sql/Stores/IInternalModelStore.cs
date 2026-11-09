using Djohnnie.SoftwareFactory.Contracts.InternalModels;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IInternalModelStore
{
    Task<List<InternalModel>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<InternalModel> SaveAsync(InternalModel value, CancellationToken cancellationToken = default);

    /// <summary>Deletes a model; refused while an internal agent uses it.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
