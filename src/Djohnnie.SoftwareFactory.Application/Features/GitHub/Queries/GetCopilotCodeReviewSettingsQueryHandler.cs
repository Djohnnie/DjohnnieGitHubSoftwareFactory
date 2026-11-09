using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetCopilotCodeReviewSettingsQueryHandler(GitHubApiClient gitHub) : IRequestHandler<GetCopilotCodeReviewSettingsQuery, Response<CopilotCodeReviewSettings>>
{
    public async Task<Response<CopilotCodeReviewSettings>> Handle(GetCopilotCodeReviewSettingsQuery request, CancellationToken cancellationToken)
    {
        return new(await gitHub.GetCopilotCodeReviewSettingsAsync(request.Dto.RepositoryFullName, cancellationToken));
    }
}
