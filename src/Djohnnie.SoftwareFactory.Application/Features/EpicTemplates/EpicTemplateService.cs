using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class EpicTemplateService(ISender mediator)
{
    public async Task<List<EpicTemplate>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetEpicTemplatesQuery(new()), cancellationToken)).Dto.Items.ToList();

    public async Task<EpicTemplate?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetEpicTemplateQuery(new(id)), cancellationToken)).Dto;

    public async Task<EpicTemplate> SaveAsync(EpicTemplate template, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new SaveEpicTemplateCommand(new(template)), cancellationToken)).Dto;

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        await mediator.Send(new DeleteEpicTemplateCommand(new(id)), cancellationToken);
}
