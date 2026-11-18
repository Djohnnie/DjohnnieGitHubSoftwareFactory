using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

public sealed class StartAgentRunCommandHandler(WorkItemWorkflow workflow) : IRequestHandler<StartAgentRunCommand, Response<AgentRun>>
{
    public async Task<Response<AgentRun>> Handle(StartAgentRunCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.StartWorkerRunAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
