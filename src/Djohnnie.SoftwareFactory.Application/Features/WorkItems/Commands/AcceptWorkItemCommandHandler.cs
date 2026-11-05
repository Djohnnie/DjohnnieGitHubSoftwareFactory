using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class AcceptWorkItemCommandHandler(WorkItemWorkflow workflow) : IRequestHandler<AcceptWorkItemCommand, Response<WorkItemAcceptResult>>
{
    public async Task<Response<WorkItemAcceptResult>> Handle(AcceptWorkItemCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.AcceptAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
