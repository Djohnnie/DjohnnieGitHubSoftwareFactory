using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>Deletes the drafts a run generated; <see cref="RepositoryItemRequest.Id"/> is the root task.</summary>
public sealed record DiscardGeneratedDraftsCommand(RepositoryItemRequest Dto) : IRequest<Response<DiscardedGeneratedDrafts>>;

public sealed class DiscardGeneratedDraftsCommandHandler(ITodoTaskStore tasks, IWorkItemStore items) : IRequestHandler<DiscardGeneratedDraftsCommand, Response<DiscardedGeneratedDrafts>>
{
    public async Task<Response<DiscardedGeneratedDrafts>> Handle(DiscardGeneratedDraftsCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        var run = await tasks.GetRunAsync(dto.RepositoryFullName, dto.Id, cancellationToken)
            ?? throw new DomainRuleException("The run no longer exists.");
        if (run.IsActive)
        {
            // Otherwise a sub-task that is still running would add items under drafts that were just deleted.
            throw new DomainRuleException("The run hasn't ended yet. Cancel it first.");
        }
        return new(await items.DeleteGeneratedDraftsAsync(dto.RepositoryFullName, dto.Id, cancellationToken));
    }
}
