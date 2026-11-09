using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IWorkItemTemplateStore
{
    Task<List<WorkItemTemplate>> GetAllAsync(WorkItemType type, CancellationToken cancellationToken = default);

    Task<WorkItemTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkItemTemplate> SaveAsync(WorkItemTemplate value, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
