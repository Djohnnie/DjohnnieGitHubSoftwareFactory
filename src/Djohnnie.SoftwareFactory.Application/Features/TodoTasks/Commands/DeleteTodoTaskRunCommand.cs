using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>Deletes a run that has ended, with all its sub-tasks; <see cref="RepositoryItemRequest.Id"/> is the root task.</summary>
public sealed record DeleteTodoTaskRunCommand(RepositoryItemRequest Dto) : IRequest<Response<OperationResult>>;

public sealed class DeleteTodoTaskRunCommandHandler(ITodoTaskStore store) : IRequestHandler<DeleteTodoTaskRunCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteTodoTaskRunCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        if (!await store.DeleteRunAsync(dto.RepositoryFullName, dto.Id, cancellationToken))
        {
            throw new DomainRuleException(await store.GetRunAsync(dto.RepositoryFullName, dto.Id, cancellationToken) is null
                ? "The run no longer exists."
                : "Only a run that has ended can be deleted. Cancel it first.");
        }
        return new(new OperationResult());
    }
}
