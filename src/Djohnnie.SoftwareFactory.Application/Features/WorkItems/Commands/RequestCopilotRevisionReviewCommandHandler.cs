using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class RequestCopilotRevisionReviewCommandHandler(CopilotRevisionReviews reviews)
    : IRequestHandler<RequestCopilotRevisionReviewCommand, Response<CopilotRevisionReviewResult>>
{
    public async Task<Response<CopilotRevisionReviewResult>> Handle(RequestCopilotRevisionReviewCommand request, CancellationToken cancellationToken)
    {
        return new(await reviews.RequestWhenFinishedAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
