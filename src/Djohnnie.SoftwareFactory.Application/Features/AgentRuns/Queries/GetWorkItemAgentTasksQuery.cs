using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>
/// The DGHSF agent worker's tasks on a work item, newest first, each with its runs; <see cref="RepositoryItemRequest.Id"/> is
/// the work item.
/// </summary>
public sealed record GetWorkItemAgentTasksQuery(RepositoryItemRequest Dto) : IRequest<Response<CollectionResult<AgentRunOverview>>>;
