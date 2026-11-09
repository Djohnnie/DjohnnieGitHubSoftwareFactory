using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GenerateChildWorkItemsQueryHandler(
    IWorkItemStore items,
    IWorkItemTemplateStore templates,
    InternalAgentRegistry registry,
    BackgroundAgents agents,
    ILogger<GenerateChildWorkItemsQueryHandler> logger) : IRequestHandler<GenerateChildWorkItemsQuery, Response<GeneratedChildWorkItems>>
{
    public async Task<Response<GeneratedChildWorkItems>> Handle(GenerateChildWorkItemsQuery request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        var parent = await items.GetAsync(dto.RepositoryFullName, dto.ParentId, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
        if (parent.Type.GeneratedChildType is not { } childType)
        {
            throw new DomainRuleException($"A {parent.Type.DisplayName.ToLowerInvariant()} can't be split into child items.");
        }
        if (string.IsNullOrWhiteSpace(parent.Description))
        {
            throw new DomainRuleException($"Describe the {parent.Type.DisplayName.ToLowerInvariant()} before generating {childType.PluralName.ToLowerInvariant()}.");
        }

        var agent = await registry.GetAsync(InternalAgentKeys.WorkItemBreakdown, cancellationToken);
        if (!agents.IsConfigured || !agent.IsEnabled)
        {
            throw new DomainRuleException("The Work Item Breakdown agent is disabled or not configured.");
        }

        var existing = await items.GetAllAsync(dto.RepositoryFullName, [childType], parent.Id, cancellationToken);
        var template = WorkItemTemplate.Proposed(await templates.GetAllAsync(childType, cancellationToken));
        try
        {
            var generated = await agents.GenerateChildrenAsync(agent, parent, childType, template, existing, cancellationToken);
            return new(new GeneratedChildWorkItems(childType, template?.Name, generated));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Includes a timeout of the model call, which surfaces as a cancellation nobody asked for.
            logger.LogWarning(ex, "The Work Item Breakdown agent could not split work item {Id}.", parent.Id);
            throw new DomainRuleException($"The Work Item Breakdown agent could not generate {childType.PluralName.ToLowerInvariant()}. Try again.");
        }
    }
}
