using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>The Copilot pull request activity of an organization's repository over the last <see cref="GetCopilotRepositoryMetricsQueryHandler.Days"/> days.</summary>
public sealed record GetCopilotRepositoryMetricsQuery(RepositoryRequest Dto) : IRequest<Response<CopilotRepositoryMetrics>>;
