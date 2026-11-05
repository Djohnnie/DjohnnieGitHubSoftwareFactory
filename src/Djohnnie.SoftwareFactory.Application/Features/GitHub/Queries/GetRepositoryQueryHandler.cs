using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetRepositoryQueryHandler(GitHubApiClient gitHub) : IRequestHandler<GetRepositoryQuery, Response<GitHubRepository>>
{
    public async Task<Response<GitHubRepository>> Handle(GetRepositoryQuery request, CancellationToken cancellationToken)
    {
        return new(await gitHub.GetRepositoryAsync(request.Dto.RepositoryFullName, cancellationToken));
    }
}
