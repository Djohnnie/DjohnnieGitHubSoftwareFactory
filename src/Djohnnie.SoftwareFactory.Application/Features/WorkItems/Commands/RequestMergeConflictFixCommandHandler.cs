using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class RequestMergeConflictFixCommandHandler(MergeConflictFixes fixes)
    : IRequestHandler<RequestMergeConflictFixCommand, Response<PullRequestFeedbackResult>>
{
    public async Task<Response<PullRequestFeedbackResult>> Handle(RequestMergeConflictFixCommand request, CancellationToken cancellationToken)
    {
        return new(await fixes.RequestAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
