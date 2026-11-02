using Djohnnie.SoftwareFactory.Contracts.Epics;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class EpicService(ISender mediator)
{
    public async Task<List<Epic>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetEpicsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items.ToList();

    public async Task<Epic?> GetAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetEpicQuery(new(repositoryFullName, id)), cancellationToken)).Dto;

    public async Task<Epic> SaveAsync(Epic epic, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new SaveEpicCommand(epic), cancellationToken)).Dto;

    public async Task DeleteAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default) =>
        await mediator.Send(new DeleteEpicCommand(new(repositoryFullName, id)), cancellationToken);

    public async Task<EpicAcceptResult> AcceptAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new AcceptEpicCommand(new(repositoryFullName, id)), cancellationToken)).Dto;

    public async Task<EpicAcceptResult> AssignAgentAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new AssignEpicAgentCommand(new(repositoryFullName, id)), cancellationToken)).Dto;
}
