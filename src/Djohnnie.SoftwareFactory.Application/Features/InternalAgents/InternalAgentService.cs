using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

/// <summary>Manages DGHSF's own chat agents and background agents: their system prompts, models and whether they are enabled.</summary>
public sealed class InternalAgentService(ISender mediator)
{
    public async Task<List<InternalAgent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetInternalAgentsQuery(new()), cancellationToken)).Dto.Items.ToList();
    }

    public async Task<InternalAgent> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetInternalAgentQuery(new(key)), cancellationToken)).Dto;
    }

    /// <summary>Saves the system prompt, the model and whether the agent is enabled.</summary>
    public async Task<InternalAgent> SaveAsync(InternalAgent agent, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveInternalAgentCommand(new(agent)), cancellationToken)).Dto;
    }

    /// <summary>A page of the model requests of one agent, or of all agents, newest first.</summary>
    public async Task<PagedResult<InternalAgentUsage>> GetUsagePageAsync(InternalAgentUsageQuery query, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetInternalAgentUsageQuery(query), cancellationToken)).Dto;
    }

    /// <summary>The most recent model requests of the agent (at most one full page), newest first.</summary>
    public async Task<IReadOnlyList<InternalAgentUsage>> GetUsageAsync(string key, CancellationToken cancellationToken = default)
    {
        return (await GetUsagePageAsync(new(key, 0, InternalAgentUsageQuery.MaxPageSize), cancellationToken)).Items;
    }

    /// <summary>Checks that the model responds on the configured endpoint; the request counts towards the usage of the agent.</summary>
    public async Task<ModelTestResult> TestModelAsync(string key, string model, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new TestInternalAgentModelQuery(new(key, model)), cancellationToken)).Dto;
    }

    /// <summary>Restores the default system prompt.</summary>
    public async Task<InternalAgent> ResetAsync(string key, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new ResetInternalAgentCommand(new(key)), cancellationToken)).Dto;
    }

    /// <summary>Adds the internal agents that are missing from the database, with their default prompts.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await mediator.Send(new SeedInternalAgentsCommand(new EmptyRequest()), cancellationToken);
    }
}
