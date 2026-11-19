using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>
/// Starts a revision run of the worker on the pull request it opened for a work item, e.g. after a revision failed;
/// <see cref="RepositoryItemRequest.Id"/> is the work item.
/// </summary>
public sealed record StartAgentRevisionCommand(RepositoryItemRequest Dto) : IRequest<Response<AgentRun>>;
