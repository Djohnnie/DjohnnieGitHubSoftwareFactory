using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed record SaveInternalAgentCommand(SaveInternalAgentRequest Dto) : IRequest<Response<InternalAgent>>;
