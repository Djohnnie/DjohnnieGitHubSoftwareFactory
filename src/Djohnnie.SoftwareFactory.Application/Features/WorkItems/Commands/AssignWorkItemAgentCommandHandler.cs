using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class AssignWorkItemAgentCommandHandler(WorkItemWorkflow workflow) : IRequestHandler<AssignWorkItemAgentCommand, Response<WorkItemAcceptResult>>
{
    public async Task<Response<WorkItemAcceptResult>> Handle(AssignWorkItemAgentCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.AssignAgentAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
