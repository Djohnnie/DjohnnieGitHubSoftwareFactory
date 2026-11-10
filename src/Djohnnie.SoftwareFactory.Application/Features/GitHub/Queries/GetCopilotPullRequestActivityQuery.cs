using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// The Copilot pull request activity of a repository over the same period as <see cref="GetCopilotRepositoryMetricsQuery"/>,
/// worked out from its pull requests and their reviews: GitHub has no Copilot usage metrics for personal accounts.
/// </summary>
public sealed record GetCopilotPullRequestActivityQuery(RepositoryRequest Dto) : IRequest<Response<CopilotRepositoryMetrics>>;
