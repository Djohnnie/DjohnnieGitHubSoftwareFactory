using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>
/// Starts and follows background task runs from the web app. The web app only writes to the database; the Tasks function app
/// dispatches and executes the tasks.
/// </summary>
public sealed class TodoTaskService(IMediator mediator)
{
    /// <summary>Starts a run that analyzes the repository and saves the work items it writes as drafts.</summary>
    public async Task<TodoTask> StartRepositoryAnalysisAsync(StartRepositoryAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new StartRepositoryAnalysisCommand(request), cancellationToken)).Dto;
    }

    public async Task<IReadOnlyList<TodoTaskRun>> GetRunsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetTodoTaskRunsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items;
    }

    public async Task<TodoTaskRun> GetRunAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetTodoTaskRunQuery(new(repositoryFullName, rootId)), cancellationToken)).Dto;
    }

    public async Task CancelRunAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new CancelTodoTaskRunCommand(new(repositoryFullName, rootId)), cancellationToken);
    }

    /// <summary>Deletes the drafts a run generated; the run must have ended.</summary>
    public async Task<DiscardedGeneratedDrafts> DiscardDraftsAsync(string repositoryFullName, Guid rootId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new DiscardGeneratedDraftsCommand(new(repositoryFullName, rootId)), cancellationToken)).Dto;
    }
}
