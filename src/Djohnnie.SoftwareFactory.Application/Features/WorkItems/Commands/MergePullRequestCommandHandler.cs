using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class MergePullRequestCommandHandler(WorkItemPullRequests pullRequests) : IRequestHandler<MergePullRequestCommand, Response<PullRequestMergeResult>>
{
    public async Task<Response<PullRequestMergeResult>> Handle(MergePullRequestCommand request, CancellationToken cancellationToken)
    {
        return new(await pullRequests.MergeAsync(request.Dto, cancellationToken));
    }
}
