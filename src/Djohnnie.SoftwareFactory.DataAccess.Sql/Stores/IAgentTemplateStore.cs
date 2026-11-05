using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IAgentTemplateStore
{
    Task<List<AgentTemplate>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<AgentTemplate?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<AgentTemplate> SaveAsync(AgentTemplate value, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
