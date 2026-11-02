using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GitHubHandlers(GitHubApiClient gitHub) :
    IRequestHandler<GetRepositoryQuery, Response<GitHubRepository>>,
    IRequestHandler<GetRepositoriesQuery, Response<CollectionResult<GitHubRepository>>>,
    IRequestHandler<GetDirectoryQuery, Response<CollectionResult<GitHubContentItem>>>,
    IRequestHandler<GetFileQuery, Response<GitHubFile?>>,
    IRequestHandler<PutFileCommand, Response<GitHubCommitResult>>,
    IRequestHandler<DeleteFileCommand, Response<GitHubCommitResult>>,
    IRequestHandler<GetCustomAgentsQuery, Response<CollectionResult<AgentSummary>>>
{
    public async Task<Response<GitHubRepository>> Handle(GetRepositoryQuery request, CancellationToken cancellationToken) =>
        new(await gitHub.GetRepositoryAsync(request.Dto.RepositoryFullName, cancellationToken));

    public async Task<Response<CollectionResult<GitHubRepository>>> Handle(GetRepositoriesQuery request, CancellationToken cancellationToken) =>
        new(new(await gitHub.GetRepositoriesAsync(cancellationToken)));

    public async Task<Response<CollectionResult<GitHubContentItem>>> Handle(GetDirectoryQuery request, CancellationToken cancellationToken) =>
        new(new(await gitHub.GetDirectoryAsync(request.Dto.RepositoryFullName, request.Dto.Path, cancellationToken)));

    public async Task<Response<GitHubFile?>> Handle(GetFileQuery request, CancellationToken cancellationToken) =>
        new(await gitHub.GetFileAsync(request.Dto.RepositoryFullName, request.Dto.Path, cancellationToken));

    public async Task<Response<GitHubCommitResult>> Handle(PutFileCommand request, CancellationToken cancellationToken) =>
        new(await gitHub.PutFileAsync(request.Dto.RepositoryFullName, request.Dto.Path, request.Dto.Content, request.Dto.Message, request.Dto.Sha, cancellationToken));

    public async Task<Response<GitHubCommitResult>> Handle(DeleteFileCommand request, CancellationToken cancellationToken) =>
        new(await gitHub.DeleteFileAsync(request.Dto.RepositoryFullName, request.Dto.Path, request.Dto.Message, request.Dto.Sha, cancellationToken));

    public async Task<Response<CollectionResult<AgentSummary>>> Handle(GetCustomAgentsQuery request, CancellationToken cancellationToken) =>
        new(new(await AgentCatalog.LoadAsync(gitHub, request.Dto.RepositoryFullName, cancellationToken)));
}
