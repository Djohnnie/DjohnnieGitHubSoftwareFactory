using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class ApprovePullRequestCommandHandler(WorkItemPullRequests pullRequests) : IRequestHandler<ApprovePullRequestCommand, Response<PullRequestFeedbackResult>>
{
    public async Task<Response<PullRequestFeedbackResult>> Handle(ApprovePullRequestCommand request, CancellationToken cancellationToken)
    {
        return new(await pullRequests.ApproveAsync(request.Dto, cancellationToken));
    }
}
