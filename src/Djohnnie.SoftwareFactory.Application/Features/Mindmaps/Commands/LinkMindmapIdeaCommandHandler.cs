using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class LinkMindmapIdeaCommandHandler(IMindmapStore mindmaps, IWorkItemStore items) : IRequestHandler<LinkMindmapIdeaCommand, Response<Mindmap>>
{
    public async Task<Response<Mindmap>> Handle(LinkMindmapIdeaCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        var done = false;
        if (dto.WorkItemId is { } workItemId)
        {
            var item = await items.GetAsync(dto.RepositoryFullName, workItemId, cancellationToken)
                ?? throw new DomainRuleException("The work item no longer exists in this repository.");
            done = item.Status == WorkItemStatus.Done;
        }
        return new(await mindmaps.LinkAsync(dto.RepositoryFullName, dto.MindmapId, dto.IdeaId, dto.WorkItemId, done, cancellationToken));
    }
}
