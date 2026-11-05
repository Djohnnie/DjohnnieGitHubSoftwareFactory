using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class SaveTemplateCommandHandler(AgentTemplateWorkflow workflow) : IRequestHandler<SaveTemplateCommand, Response<AgentTemplate>>
{
    public async Task<Response<AgentTemplate>> Handle(SaveTemplateCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.SaveAsync(request.Dto.Id, request.Dto.FileName, request.Dto.Agent, cancellationToken));
    }
}
