using Djohnnie.SoftwareFactory.Contracts.InternalAgents;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IInternalAgentStore
{
    /// <remarks><see cref="InternalAgent.DefaultInstructions"/> is not filled in by the store.</remarks>
    Task<List<InternalAgent>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<InternalAgent?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Updates the system prompt and whether the agent is enabled; the other fields belong to the code.</summary>
    Task<InternalAgent> SaveAsync(InternalAgent value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the agents that don't exist yet with their default prompt, and refreshes the name, description and kind
    /// of existing ones. Existing prompts and enabled flags are kept.
    /// </summary>
    /// <returns>The number of added agents.</returns>
    Task<int> EnsureAsync(IReadOnlyList<InternalAgent> defaults, CancellationToken cancellationToken = default);
}
