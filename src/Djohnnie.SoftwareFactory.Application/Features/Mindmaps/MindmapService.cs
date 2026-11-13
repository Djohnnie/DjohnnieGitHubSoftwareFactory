using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

/// <summary>The mindmaps (simple to-do lists of ideas) of each repository.</summary>
public sealed class MindmapService(ISender mediator)
{
    public async Task<List<MindmapSummary>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetMindmapsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items.ToList();
    }

    public async Task<Mindmap?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetMindmapQuery(new(repositoryFullName, id)), cancellationToken)).Dto;
    }

    /// <summary>Creates (empty id) or replaces a mindmap; the texts are trimmed.</summary>
    public async Task<Mindmap> SaveAsync(Mindmap mindmap, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveMindmapCommand(new(mindmap)), cancellationToken)).Dto;
    }

    /// <summary>Links an idea of a stored mindmap to a work item (an idea of a closed item is marked done), or unlinks it with <c>null</c>.</summary>
    public async Task<Mindmap> LinkAsync(string repositoryFullName, Guid mindmapId, Guid ideaId, Guid? workItemId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new LinkMindmapIdeaCommand(new(repositoryFullName, mindmapId, ideaId, workItemId)), cancellationToken)).Dto;
    }

    /// <summary>Lets the Mindmap Work Item Writer write a new work item for an idea; nothing is stored.</summary>
    public async Task<GeneratedWorkItem> GenerateWorkItemAsync(GenerateMindmapWorkItemRequest request, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GenerateMindmapWorkItemQuery(request), cancellationToken)).Dto;
    }

    /// <summary>The mindmap ideas linked to a work item, by mindmap title.</summary>
    public async Task<List<MindmapIdeaLink>> GetLinkedIdeasAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetLinkedMindmapIdeasQuery(new(repositoryFullName, workItemId)), cancellationToken)).Dto.Items.ToList();
    }

    public async Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteMindmapCommand(new(repositoryFullName, id)), cancellationToken);
    }
}
