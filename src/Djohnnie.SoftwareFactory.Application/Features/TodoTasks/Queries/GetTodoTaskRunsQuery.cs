using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>The newest runs of a repository, newest first.</summary>
public sealed record GetTodoTaskRunsQuery(RepositoryRequest Dto) : IRequest<Response<CollectionResult<TodoTaskRun>>>;

public sealed class GetTodoTaskRunsQueryHandler(ITodoTaskStore store) : IRequestHandler<GetTodoTaskRunsQuery, Response<CollectionResult<TodoTaskRun>>>
{
    public const int MaxRuns = 25;

    public async Task<Response<CollectionResult<TodoTaskRun>>> Handle(GetTodoTaskRunsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetRunsAsync(request.Dto.RepositoryFullName, MaxRuns, cancellationToken)));
    }
}

/// <summary>One run with all its tasks; <see cref="RepositoryItemRequest.Id"/> is the root task.</summary>
public sealed record GetTodoTaskRunQuery(RepositoryItemRequest Dto) : IRequest<Response<TodoTaskRun>>;

public sealed class GetTodoTaskRunQueryHandler(ITodoTaskStore store) : IRequestHandler<GetTodoTaskRunQuery, Response<TodoTaskRun>>
{
    public async Task<Response<TodoTaskRun>> Handle(GetTodoTaskRunQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetRunAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken)
            ?? throw new DomainRuleException("The run no longer exists."));
    }
}
