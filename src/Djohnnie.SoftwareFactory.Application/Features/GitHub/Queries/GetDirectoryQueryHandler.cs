using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetDirectoryQueryHandler(GitHubApiClient gitHub) : IRequestHandler<GetDirectoryQuery, Response<CollectionResult<GitHubContentItem>>>
{
    public async Task<Response<CollectionResult<GitHubContentItem>>> Handle(GetDirectoryQuery request, CancellationToken cancellationToken)
    {
        return new(new(await gitHub.GetDirectoryAsync(request.Dto.RepositoryFullName, request.Dto.Path, cancellationToken)));
    }
}
