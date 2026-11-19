using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class GenerateMindmapPlanQueryHandler(
    IMindmapStore mindmaps,
    IWorkItemStore items,
    IWorkItemTemplateStore templates,
    InternalAgentRegistry registry,
    BackgroundAgents agents,
    ILogger<GenerateMindmapPlanQueryHandler> logger) : IRequestHandler<GenerateMindmapPlanQuery, Response<GeneratedMindmapPlan>>
{
    public async Task<Response<GeneratedMindmapPlan>> Handle(GenerateMindmapPlanQuery request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        var mindmap = await mindmaps.GetAsync(dto.RepositoryFullName, dto.MindmapId, cancellationToken)
            ?? throw new DomainRuleException("The mindmap no longer exists.");

        var agent = await registry.GetAsync(InternalAgentKeys.MindmapPlanner, cancellationToken);
        if (!agents.IsConfigured || !agent.IsEnabled)
        {
            throw new DomainRuleException("The Mindmap Planner agent is disabled or not configured.");
        }

        var linked = await MindmapPlanRules.LinkedItemsAsync(items, mindmap, cancellationToken);
        var existingEpic = MindmapPlanRules.ExistingEpic(mindmap, linked);
        var existingChildren = existingEpic is null
            ? []
            : await items.GetAllAsync(dto.RepositoryFullName, parentId: existingEpic.Id, cancellationToken: cancellationToken);
        var typeTemplates = new Dictionary<WorkItemType, WorkItemTemplate>();
        foreach (var type in WorkItemTypeRules.All)
        {
            if (WorkItemTemplate.Proposed(await templates.GetAllAsync(type, cancellationToken)) is { } template)
            {
                typeTemplates[type] = template;
            }
        }

        List<MindmapPlanItem> plan;
        try
        {
            var proposed = await agents.PlanMindmapAsync(agent, mindmap, linked, existingEpic, existingChildren, typeTemplates, cancellationToken);
            plan = MindmapPlanRules.Filter(mindmap, linked, proposed);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not DomainRuleException)
        {
            // Includes a timeout of the model call, which surfaces as a cancellation nobody asked for.
            logger.LogWarning(ex, "The Mindmap Planner could not plan mindmap {Id}.", mindmap.Id);
            throw new DomainRuleException("The Mindmap Planner could not generate the work items. Try again.");
        }
        if (existingEpic is null && plan.Count == 0)
        {
            throw new DomainRuleException("The Mindmap Planner did not write the epic for the central idea. Try again.");
        }
        return new(new GeneratedMindmapPlan(existingEpic?.Id, plan));
    }
}
