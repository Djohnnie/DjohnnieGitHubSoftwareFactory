using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class WorkItemTemplateService(ISender mediator)
{
    /// <summary>The templates of one work item type, alphabetically.</summary>
    public async Task<List<WorkItemTemplate>> GetAllAsync(WorkItemType type, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetWorkItemTemplatesQuery(new(type)), cancellationToken)).Dto.Items.ToList();
    }

    public async Task<WorkItemTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetWorkItemTemplateQuery(new(id)), cancellationToken)).Dto;
    }

    public async Task<WorkItemTemplate> SaveAsync(WorkItemTemplate template, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveWorkItemTemplateCommand(new(template)), cancellationToken)).Dto;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteWorkItemTemplateCommand(new(id)), cancellationToken);
    }
}
