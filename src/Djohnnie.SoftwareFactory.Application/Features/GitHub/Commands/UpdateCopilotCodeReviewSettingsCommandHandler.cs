using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class UpdateCopilotCodeReviewSettingsCommandHandler(GitHubApiClient gitHub) : IRequestHandler<UpdateCopilotCodeReviewSettingsCommand, Response<CopilotCodeReviewSettings>>
{
    public async Task<Response<CopilotCodeReviewSettings>> Handle(UpdateCopilotCodeReviewSettingsCommand request, CancellationToken cancellationToken)
    {
        return new(await gitHub.UpdateCopilotCodeReviewSettingsAsync(request.Dto.RepositoryFullName, request.Dto.Changes, cancellationToken));
    }
}
