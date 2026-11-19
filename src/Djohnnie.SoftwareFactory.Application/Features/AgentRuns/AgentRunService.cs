using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>
/// Follows and controls the DGHSF agent worker's runs from the web app. The web app only writes to the database; the Tasks
/// function app dispatches the runs and the worker's Container Apps job executes them.
/// </summary>
public sealed class AgentRunService(IMediator mediator)
{
    /// <summary>The runs of a work item, newest first.</summary>
    public async Task<IReadOnlyList<AgentRun>> GetRunsAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetAgentRunsQuery(new(repositoryFullName, workItemId)), cancellationToken)).Dto.Items;
    }

    /// <summary>The newest runs of a repository (at most <see cref="GetRepositoryAgentRunsQueryHandler.MaxRuns"/>), newest first, with their work items.</summary>
    public async Task<IReadOnlyList<AgentRunOverview>> GetRepositoryRunsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetRepositoryAgentRunsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items;
    }

    /// <summary>Starts another run on a work item that is in progress, e.g. after a failed run.</summary>
    public async Task<AgentRun> StartRunAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new StartAgentRunCommand(new(repositoryFullName, workItemId)), cancellationToken)).Dto;
    }

    /// <summary>Starts a revision run on the pull request the worker opened, e.g. after a revision failed; it addresses the feedback that wasn't handled yet.</summary>
    public async Task<AgentRun> StartRevisionAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new StartAgentRevisionCommand(new(repositoryFullName, workItemId)), cancellationToken)).Dto;
    }

    public async Task CancelRunAsync(string repositoryFullName, Guid runId, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new CancelAgentRunCommand(new(repositoryFullName, runId)), cancellationToken);
    }
}
