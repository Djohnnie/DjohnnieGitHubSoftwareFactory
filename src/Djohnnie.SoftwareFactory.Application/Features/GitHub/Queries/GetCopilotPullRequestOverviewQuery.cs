using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>The repository's open pull requests and the Copilot cloud agent sessions running in it right now.</summary>
public sealed record GetCopilotPullRequestOverviewQuery(RepositoryRequest Dto) : IRequest<Response<CopilotPullRequestOverview>>;
