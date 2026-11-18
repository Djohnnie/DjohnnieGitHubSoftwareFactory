using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>Cancels a worker run that hasn't ended; <see cref="RepositoryItemRequest.Id"/> is the run.</summary>
public sealed record CancelAgentRunCommand(RepositoryItemRequest Dto) : IRequest<Response<OperationResult>>;
