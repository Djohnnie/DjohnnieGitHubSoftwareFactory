using Djohnnie.SoftwareFactory.Contracts.Skills;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface ISkillStore
{
    /// <summary>All skills, sorted by name.</summary>
    Task<List<Skill>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Skill?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates (empty id) or updates a skill.</summary>
    /// <exception cref="Contracts.DomainRuleException">Another skill has the same name, or the skill no longer exists.</exception>
    Task<Skill> SaveAsync(Skill value, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
