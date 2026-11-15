using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>Cancels the tasks of a run that haven't ended; <see cref="RepositoryItemRequest.Id"/> is the root task.</summary>
public sealed record CancelTodoTaskRunCommand(RepositoryItemRequest Dto) : IRequest<Response<OperationResult>>;

public sealed class CancelTodoTaskRunCommandHandler(ITodoTaskStore store) : IRequestHandler<CancelTodoTaskRunCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(CancelTodoTaskRunCommand request, CancellationToken cancellationToken)
    {
        await store.CancelRunAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
        return new(new OperationResult());
    }
}
