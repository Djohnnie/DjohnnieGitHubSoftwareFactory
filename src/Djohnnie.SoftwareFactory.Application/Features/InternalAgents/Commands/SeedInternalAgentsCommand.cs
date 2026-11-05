using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed record SeedInternalAgentsCommand(EmptyRequest Dto) : IRequest<Response<OperationResult>>;
