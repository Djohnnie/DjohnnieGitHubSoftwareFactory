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

    /// <summary>Starts another run on a work item that is in progress, e.g. after a failed run.</summary>
    public async Task<AgentRun> StartRunAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new StartAgentRunCommand(new(repositoryFullName, workItemId)), cancellationToken)).Dto;
    }

    public async Task CancelRunAsync(string repositoryFullName, Guid runId, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new CancelAgentRunCommand(new(repositoryFullName, runId)), cancellationToken);
    }
}
