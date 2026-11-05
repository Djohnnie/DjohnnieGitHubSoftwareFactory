using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IEpicTemplateStore
{
    Task<List<EpicTemplate>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<EpicTemplate?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<EpicTemplate> SaveAsync(EpicTemplate value, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
