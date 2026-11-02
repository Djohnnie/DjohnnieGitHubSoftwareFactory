using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class AgentTemplateService(ISender mediator)
{
    public async Task<List<AgentTemplate>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetTemplatesQuery(new()), cancellationToken)).Dto.Items.ToList();

    public async Task<AgentTemplate?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetTemplateQuery(new(id)), cancellationToken)).Dto;

    public async Task<AgentTemplate> SaveAsync(int id, string fileName, AgentDefinition agent, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new SaveTemplateCommand(new(id, fileName, agent)), cancellationToken)).Dto;

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        await mediator.Send(new DeleteTemplateCommand(new(id)), cancellationToken);

    public async Task<List<AgentTemplateApplyResult>> ApplyAsync(
        AgentTemplate template, IEnumerable<string> repositories, string fileName, bool overwrite,
        string? commitMessage = null, IProgress<AgentTemplateApplyResult>? progress = null, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new ApplyTemplateCommand(new(template, repositories.ToList(), fileName, overwrite, commitMessage), progress), cancellationToken)).Dto.Items.ToList();
}
