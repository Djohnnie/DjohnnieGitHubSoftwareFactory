using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed record DeleteModelCommand(EntityIdRequest Dto) : IRequest<Response<OperationResult>>;
