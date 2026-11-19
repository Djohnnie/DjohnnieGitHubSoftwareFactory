using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

public sealed class StartAgentRevisionCommandHandler(WorkItemWorkflow workflow) : IRequestHandler<StartAgentRevisionCommand, Response<AgentRun>>
{
    public async Task<Response<AgentRun>> Handle(StartAgentRevisionCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.StartRevisionRunAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
