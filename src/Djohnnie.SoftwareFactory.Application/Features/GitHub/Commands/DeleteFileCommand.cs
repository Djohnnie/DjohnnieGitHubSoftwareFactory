using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed record DeleteFileCommand(DeleteFileRequest Dto) : IRequest<Response<GitHubCommitResult>>;
