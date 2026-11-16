using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed record DeleteKnowledgeFileCommand(EntityIdRequest Dto) : IRequest<Response<OperationResult>>;
