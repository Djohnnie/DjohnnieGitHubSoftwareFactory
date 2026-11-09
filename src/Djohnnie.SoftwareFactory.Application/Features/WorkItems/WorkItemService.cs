using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class WorkItemService(ISender mediator)
{
    /// <summary>The items of a repository, optionally only of some types and/or the children of one parent.</summary>
    public async Task<List<WorkItem>> GetAllAsync(string repositoryFullName, IReadOnlyList<WorkItemType>? types = null, Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetWorkItemsQuery(new(repositoryFullName, types, parentId)), cancellationToken)).Dto.Items.ToList();
    }

    public async Task<WorkItem?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetWorkItemQuery(new(repositoryFullName, id)), cancellationToken)).Dto;
    }

    public async Task<WorkItem> SaveAsync(WorkItem item, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveWorkItemCommand(item), cancellationToken)).Dto;
    }

    public async Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteWorkItemCommand(new(repositoryFullName, id)), cancellationToken);
    }

    /// <summary>
    /// Creates the labelled GitHub issue, links it to its parent and children as sub-issues and, for stories,
    /// bugs and tasks, assigns the Copilot cloud agent.
    /// </summary>
    public async Task<WorkItemAcceptResult> AcceptAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new AcceptWorkItemCommand(new(repositoryFullName, id)), cancellationToken)).Dto;
    }

    /// <summary>Asks the Model Selector which enabled model fits the complexity of the (unsaved) item; nothing is stored.</summary>
    /// <returns><c>null</c> when the Model Selector is disabled or not configured, or picked nothing usable.</returns>
    public async Task<ModelSelection?> SelectModelAsync(WorkItem item, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SelectWorkItemModelQuery(new(item)), cancellationToken)).Dto.Selection;
    }

    /// <summary>Lets the Work Item Breakdown agent propose the children of a stored epic, feature or user story; nothing is stored.</summary>
    public async Task<GeneratedChildWorkItems> GenerateChildrenAsync(string repositoryFullName, Guid parentId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GenerateChildWorkItemsQuery(new(repositoryFullName, parentId)), cancellationToken)).Dto;
    }

    /// <summary>Saves proposed children as drafts under their parent, all or none.</summary>
    public async Task<List<WorkItem>> SaveGeneratedChildrenAsync(string repositoryFullName, Guid parentId, IReadOnlyList<GeneratedWorkItem> items, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveGeneratedWorkItemsCommand(new(repositoryFullName, parentId, items)), cancellationToken)).Dto.Items.ToList();
    }

    public async Task<WorkItemAcceptResult> AssignAgentAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new AssignWorkItemAgentCommand(new(repositoryFullName, id)), cancellationToken)).Dto;
    }

    /// <summary>Stores the current title, description and state of an accepted item's GitHub issue.</summary>
    public async Task<WorkItemRefreshResult> RefreshAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new RefreshWorkItemCommand(new(repositoryFullName, id)), cancellationToken)).Dto;
    }
}
