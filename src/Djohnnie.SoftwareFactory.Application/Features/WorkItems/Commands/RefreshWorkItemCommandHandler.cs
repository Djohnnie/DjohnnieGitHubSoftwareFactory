using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <remarks>
/// Both the Refresh button and the sync come here, so this is where a closed item marks the mindmap ideas linked to it
/// as done. Failing to update the mindmaps is logged and never fails the refresh.
/// </remarks>
public sealed class RefreshWorkItemCommandHandler(
    WorkItemWorkflow workflow,
    IMindmapStore mindmaps,
    ILogger<RefreshWorkItemCommandHandler> logger) : IRequestHandler<RefreshWorkItemCommand, Response<WorkItemRefreshResult>>
{
    public async Task<Response<WorkItemRefreshResult>> Handle(RefreshWorkItemCommand request, CancellationToken cancellationToken)
    {
        var result = await workflow.RefreshFromIssueAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
        if (result.WorkItem.Status == WorkItemStatus.Done)
        {
            try
            {
                var marked = await mindmaps.MarkWorkItemDoneAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
                if (marked > 0)
                {
                    logger.LogInformation("Marked {Count} mindmap ideas linked to work item {Id} as done.", marked, request.Dto.Id);
                }
            }
            catch (PersistenceException ex)
            {
                logger.LogWarning(ex, "Could not mark the mindmap ideas linked to work item {Id} as done.", request.Dto.Id);
            }
        }
        return new(result);
    }
}
