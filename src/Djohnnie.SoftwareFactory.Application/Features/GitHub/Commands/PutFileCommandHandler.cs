using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class PutFileCommandHandler(GitHubApiClient gitHub) : IRequestHandler<PutFileCommand, Response<GitHubCommitResult>>
{
    public async Task<Response<GitHubCommitResult>> Handle(PutFileCommand request, CancellationToken cancellationToken)
    {
        return new(await gitHub.PutFileAsync(request.Dto.RepositoryFullName, request.Dto.Path, request.Dto.Content, request.Dto.Message, request.Dto.Sha, cancellationToken));
    }
}
