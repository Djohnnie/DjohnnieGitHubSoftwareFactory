using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

public sealed record GetCustomAgentsQuery(RepositoryRequest Dto) : IRequest<Response<CollectionResult<AgentSummary>>>;
