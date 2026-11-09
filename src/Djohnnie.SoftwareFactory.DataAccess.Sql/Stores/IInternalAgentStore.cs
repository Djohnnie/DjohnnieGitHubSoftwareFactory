using Djohnnie.SoftwareFactory.Contracts.InternalAgents;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public interface IInternalAgentStore
{
    /// <remarks><see cref="InternalAgent.DefaultInstructions"/> is not filled in by the store.</remarks>
    Task<List<InternalAgent>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<InternalAgent?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Updates the system prompt, the model and whether the agent is enabled; the other fields belong to the code.</summary>
    Task<InternalAgent> SaveAsync(InternalAgent value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores one request of the agent, priced at what the catalog says now (free when <paramref name="model"/> isn't in
    /// it), and adds it to the agent's running totals in the same save.
    /// </summary>
    /// <param name="key">The agent.</param>
    /// <param name="model">The name of the model that ran.</param>
    /// <param name="usage">The tokens of the request.</param>
    /// <param name="source">What caused the request.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    Task RecordUsageAsync(string key, string model, ModelUsage usage, InternalAgentUsageSource source, CancellationToken cancellationToken = default);

    /// <summary>The most recent requests, newest first.</summary>
    /// <param name="key">The agent; <c>null</c> for the requests of all agents.</param>
    /// <param name="count">The maximum number of requests.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<List<InternalAgentUsage>> GetRecentUsageAsync(string? key, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the agents that don't exist yet with their default prompt and the default model, and refreshes the name,
    /// description and kind of existing ones. Existing prompts, models and enabled flags are kept.
    /// </summary>
    /// <returns>The number of added agents.</returns>
    Task<int> EnsureAsync(IReadOnlyList<InternalAgent> defaults, CancellationToken cancellationToken = default);
}
