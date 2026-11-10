using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class SendPullRequestFeedbackCommandHandler(WorkItemPullRequests pullRequests) : IRequestHandler<SendPullRequestFeedbackCommand, Response<PullRequestFeedbackResult>>
{
    public async Task<Response<PullRequestFeedbackResult>> Handle(SendPullRequestFeedbackCommand request, CancellationToken cancellationToken)
    {
        return new(await pullRequests.SendFeedbackAsync(request.Dto, cancellationToken));
    }
}
