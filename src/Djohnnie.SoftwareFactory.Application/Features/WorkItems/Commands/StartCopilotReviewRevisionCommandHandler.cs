using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class StartCopilotReviewRevisionCommandHandler(CopilotReviewRevisions revisions)
    : IRequestHandler<StartCopilotReviewRevisionCommand, Response<CopilotReviewRevisionResult>>
{
    public async Task<Response<CopilotReviewRevisionResult>> Handle(StartCopilotReviewRevisionCommand request, CancellationToken cancellationToken)
    {
        return new(await revisions.StartWhenReviewedAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
