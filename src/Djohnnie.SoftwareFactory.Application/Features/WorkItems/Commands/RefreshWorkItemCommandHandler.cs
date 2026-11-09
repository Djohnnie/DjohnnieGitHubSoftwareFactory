using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class RefreshWorkItemCommandHandler(WorkItemWorkflow workflow) : IRequestHandler<RefreshWorkItemCommand, Response<WorkItemRefreshResult>>
{
    public async Task<Response<WorkItemRefreshResult>> Handle(RefreshWorkItemCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.RefreshFromIssueAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
