using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetFileQueryHandler(GitHubApiClient gitHub) : IRequestHandler<GetFileQuery, Response<GitHubFile?>>
{
    public async Task<Response<GitHubFile?>> Handle(GetFileQuery request, CancellationToken cancellationToken)
    {
        return new(await gitHub.GetFileAsync(request.Dto.RepositoryFullName, request.Dto.Path, cancellationToken));
    }
}
