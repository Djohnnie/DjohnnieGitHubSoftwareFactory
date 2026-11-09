using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

/// <summary>
/// Combines the fixed set of internal agents in code (<see cref="InternalAgentDefaults"/>) with their stored prompts.
/// Agents that are not stored yet are seeded with their defaults.
/// </summary>
public sealed class InternalAgentRegistry(IInternalAgentStore store)
{
    /// <summary>All internal agents in catalog order; seeds the missing ones first.</summary>
    public async Task<List<InternalAgent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var stored = (await store.GetAllAsync(cancellationToken)).ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);
        return InternalAgentDefaults.All
            .Select(defaults => Merge(defaults, stored.GetValueOrDefault(defaults.Key)))
            .ToList();
    }

    /// <summary>The agent with its stored prompt, or with its default prompt when it is not stored (yet).</summary>
    public async Task<InternalAgent> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var defaults = InternalAgentDefaults.Find(key) ?? throw new DomainRuleException($"Unknown internal agent '{key}'.");
        return Merge(defaults, await store.GetAsync(defaults.Key, cancellationToken));
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        await store.EnsureAsync(InternalAgentDefaults.All, cancellationToken);
    }

    private static InternalAgent Merge(InternalAgent defaults, InternalAgent? stored)
    {
        return stored is null
            ? defaults.Copy()
            : defaults with
            {
                Id = stored.Id,
                Instructions = stored.Instructions,
                Model = stored.Model,
                IsEnabled = stored.IsEnabled,
                CreatedAt = stored.CreatedAt,
                UpdatedAt = stored.UpdatedAt
            };
    }
}
