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

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class GenerateMindmapWorkItemQueryHandler(
    IMindmapStore mindmaps,
    IWorkItemStore items,
    IWorkItemTemplateStore templates,
    InternalAgentRegistry registry,
    BackgroundAgents agents,
    ILogger<GenerateMindmapWorkItemQueryHandler> logger) : IRequestHandler<GenerateMindmapWorkItemQuery, Response<GeneratedWorkItem>>
{
    public async Task<Response<GeneratedWorkItem>> Handle(GenerateMindmapWorkItemQuery request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        var mindmap = await mindmaps.GetAsync(dto.RepositoryFullName, dto.MindmapId, cancellationToken)
            ?? throw new DomainRuleException("The mindmap no longer exists.");
        if (mindmap.Find(dto.IdeaId) is null)
        {
            throw new DomainRuleException("The idea no longer exists in the mindmap.");
        }

        var agent = await registry.GetAsync(InternalAgentKeys.MindmapWorkItemWriter, cancellationToken);
        if (!agents.IsConfigured || !agent.IsEnabled)
        {
            throw new DomainRuleException("The Mindmap Work Item Writer agent is disabled or not configured.");
        }

        var linkedIds = mindmap.Nodes.Where(n => n.WorkItemId is not null).Select(n => n.WorkItemId!.Value).ToHashSet();
        var linked = linkedIds.Count == 0
            ? new Dictionary<Guid, WorkItem>()
            : (await items.GetAllAsync(dto.RepositoryFullName, cancellationToken: cancellationToken))
                .Where(item => linkedIds.Contains(item.Id)).ToDictionary(item => item.Id);
        var template = dto.TemplateId is { } templateId ? await templates.GetAsync(templateId, cancellationToken) : null;
        if (template is not null && template.Type != dto.Type)
        {
            template = null;
        }
        var parent = dto.ParentId is { } parentId ? await items.GetAsync(dto.RepositoryFullName, parentId, cancellationToken) : null;

        GeneratedWorkItem? generated;
        try
        {
            generated = await agents.WriteMindmapWorkItemAsync(agent, mindmap, dto.IdeaId, dto.Type, linked, template, parent, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Includes a timeout of the model call, which surfaces as a cancellation nobody asked for.
            logger.LogWarning(ex, "The Mindmap Work Item Writer could not write a {Type} for idea {Idea}.", dto.Type, dto.IdeaId);
            generated = null;
        }
        return new(generated ?? throw new DomainRuleException(
            $"The Mindmap Work Item Writer could not write the {dto.Type.DisplayName.ToLowerInvariant()}. Try again."));
    }
}
