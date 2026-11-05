using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetRepositoriesQueryHandler(GitHubApiClient gitHub) : IRequestHandler<GetRepositoriesQuery, Response<CollectionResult<GitHubRepository>>>
{
    public async Task<Response<CollectionResult<GitHubRepository>>> Handle(GetRepositoriesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await gitHub.GetRepositoriesAsync(cancellationToken)));
    }
}
