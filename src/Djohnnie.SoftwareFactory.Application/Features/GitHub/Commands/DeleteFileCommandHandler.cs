using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class DeleteFileCommandHandler(GitHubApiClient gitHub) : IRequestHandler<DeleteFileCommand, Response<GitHubCommitResult>>
{
    public async Task<Response<GitHubCommitResult>> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        return new(await gitHub.DeleteFileAsync(request.Dto.RepositoryFullName, request.Dto.Path, request.Dto.Message, request.Dto.Sha, cancellationToken));
    }
}
