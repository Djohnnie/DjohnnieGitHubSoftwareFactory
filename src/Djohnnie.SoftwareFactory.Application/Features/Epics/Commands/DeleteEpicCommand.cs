using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed record DeleteEpicCommand(RepositoryItemRequest Dto) : IRequest<Response<OperationResult>>;
