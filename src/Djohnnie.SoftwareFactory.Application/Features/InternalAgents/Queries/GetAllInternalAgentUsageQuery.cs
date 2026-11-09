using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed record GetAllInternalAgentUsageQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<InternalAgentUsage>>>;
