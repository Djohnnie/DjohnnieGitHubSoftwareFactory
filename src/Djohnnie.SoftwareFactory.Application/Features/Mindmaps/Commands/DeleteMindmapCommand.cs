using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed record DeleteMindmapCommand(RepositoryItemRequest Dto) : IRequest<Response<OperationResult>>;
