using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed record GetEpicsQuery(RepositoryRequest Dto) : IRequest<Response<CollectionResult<Epic>>>;
public sealed record GetEpicQuery(RepositoryItemRequest Dto) : IRequest<Response<Epic?>>;
public sealed record SaveEpicCommand(Epic Dto) : IRequest<Response<Epic>>;
public sealed record DeleteEpicCommand(RepositoryItemRequest Dto) : IRequest<Response<OperationResult>>;
public sealed record AcceptEpicCommand(RepositoryItemRequest Dto) : IRequest<Response<EpicAcceptResult>>;
public sealed record AssignEpicAgentCommand(RepositoryItemRequest Dto) : IRequest<Response<EpicAcceptResult>>;
