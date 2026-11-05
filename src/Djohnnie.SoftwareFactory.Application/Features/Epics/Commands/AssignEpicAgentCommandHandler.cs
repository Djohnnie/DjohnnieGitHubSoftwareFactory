using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class AssignEpicAgentCommandHandler(EpicWorkflow workflow) : IRequestHandler<AssignEpicAgentCommand, Response<EpicAcceptResult>>
{
    public async Task<Response<EpicAcceptResult>> Handle(AssignEpicAgentCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.AssignAgentAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
