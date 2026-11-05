using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed record AssignWorkItemAgentCommand(RepositoryItemRequest Dto) : IRequest<Response<WorkItemAcceptResult>>;
