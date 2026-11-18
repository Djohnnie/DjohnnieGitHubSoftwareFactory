using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>The newest DGHSF agent worker runs of a repository, newest first, with their work items.</summary>
public sealed record GetRepositoryAgentRunsQuery(RepositoryRequest Dto) : IRequest<Response<CollectionResult<AgentRunOverview>>>;
