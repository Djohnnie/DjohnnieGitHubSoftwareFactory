using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed record GetRepositoriesQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<GitHubRepository>>>;
public sealed record GetRepositoryQuery(RepositoryRequest Dto) : IRequest<Response<GitHubRepository>>;
public sealed record GetDirectoryQuery(RepositoryPathRequest Dto) : IRequest<Response<CollectionResult<GitHubContentItem>>>;
public sealed record GetFileQuery(RepositoryPathRequest Dto) : IRequest<Response<GitHubFile?>>;
public sealed record PutFileCommand(PutFileRequest Dto) : IRequest<Response<GitHubCommitResult>>;
public sealed record DeleteFileCommand(DeleteFileRequest Dto) : IRequest<Response<GitHubCommitResult>>;
public sealed record GetCustomAgentsQuery(RepositoryRequest Dto) : IRequest<Response<CollectionResult<AgentSummary>>>;
