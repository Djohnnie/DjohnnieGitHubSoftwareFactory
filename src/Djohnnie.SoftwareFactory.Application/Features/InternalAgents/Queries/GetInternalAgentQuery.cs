using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed record GetInternalAgentQuery(InternalAgentKeyRequest Dto) : IRequest<Response<InternalAgent>>;
