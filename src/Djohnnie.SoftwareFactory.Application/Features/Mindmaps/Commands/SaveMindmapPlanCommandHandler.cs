using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class SaveMindmapPlanCommandHandler(IMindmapStore mindmaps, IWorkItemStore items) : IRequestHandler<SaveMindmapPlanCommand, Response<CollectionResult<WorkItem>>>
{
    public async Task<Response<CollectionResult<WorkItem>>> Handle(SaveMindmapPlanCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        var mindmap = await mindmaps.GetAsync(dto.RepositoryFullName, dto.MindmapId, cancellationToken)
            ?? throw new DomainRuleException("The mindmap no longer exists.");
        var linked = await MindmapPlanRules.LinkedItemsAsync(items, mindmap, cancellationToken);
        if (MindmapPlanRules.Problem(mindmap, linked, dto.Items) is { } problem)
        {
            throw new DomainRuleException(problem);
        }
        return new(new(await mindmaps.AddPlannedWorkItemsAsync(dto.RepositoryFullName, dto.MindmapId, dto.Items, cancellationToken)));
    }
}
