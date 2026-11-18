using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>The DGHSF agent worker's runs of a work item, newest first; <see cref="RepositoryItemRequest.Id"/> is the work item.</summary>
public sealed record GetAgentRunsQuery(RepositoryItemRequest Dto) : IRequest<Response<CollectionResult<AgentRun>>>;
