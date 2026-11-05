using Djohnnie.SoftwareFactory.Contracts.Epics;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IEpicStore
{
    Task<List<Epic>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default);

    Task<Epic?> GetAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default);

    Task<Epic> SaveAsync(Epic epic, CancellationToken cancellationToken = default);

    Task DeleteAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default);

    Task<Epic> MarkAcceptedAsync(string repositoryFullName, int id, int issueNumber, string issueUrl, CancellationToken cancellationToken);

    Task<Epic> MarkAssignedAsync(string repositoryFullName, int id, CancellationToken cancellationToken);
}
