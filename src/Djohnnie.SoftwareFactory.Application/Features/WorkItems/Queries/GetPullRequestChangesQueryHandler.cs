using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GetPullRequestChangesQueryHandler(WorkItemPullRequests pullRequests) : IRequestHandler<GetPullRequestChangesQuery, Response<PullRequestChanges>>
{
    public async Task<Response<PullRequestChanges>> Handle(GetPullRequestChangesQuery request, CancellationToken cancellationToken)
    {
        return new(await pullRequests.GetChangesAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
