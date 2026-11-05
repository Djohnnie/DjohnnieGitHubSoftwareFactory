using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class ApplyTemplateCommandHandler(AgentTemplateWorkflow workflow) : IRequestHandler<ApplyTemplateCommand, Response<CollectionResult<AgentTemplateApplyResult>>>
{
    public async Task<Response<CollectionResult<AgentTemplateApplyResult>>> Handle(ApplyTemplateCommand request, CancellationToken cancellationToken)
    {
        return new(new(await workflow.ApplyAsync(request.Dto.Template, request.Dto.Repositories, request.Dto.FileName,
            request.Dto.Overwrite, request.Dto.CommitMessage, request.Progress, cancellationToken)));
    }
}
