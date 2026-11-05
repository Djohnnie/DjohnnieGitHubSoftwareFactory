using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

/// <summary>Returns the ids of the favorite agents among the synced ones.</summary>
public sealed record SyncAgentsCommand(SyncAgentsRequest Dto) : IRequest<Response<CollectionResult<string>>>;
public sealed record SetAgentFavoriteCommand(SetAgentFavoriteRequest Dto) : IRequest<Response<OperationResult>>;
