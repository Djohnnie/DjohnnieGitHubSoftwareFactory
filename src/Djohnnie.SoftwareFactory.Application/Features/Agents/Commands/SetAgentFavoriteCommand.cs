using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

public sealed record SetAgentFavoriteCommand(SetAgentFavoriteRequest Dto) : IRequest<Response<OperationResult>>;
