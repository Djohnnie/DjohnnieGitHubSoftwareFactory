using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>Starts another worker run on a work item that is in progress; <see cref="RepositoryItemRequest.Id"/> is the work item.</summary>
public sealed record StartAgentRunCommand(RepositoryItemRequest Dto) : IRequest<Response<AgentRun>>;
