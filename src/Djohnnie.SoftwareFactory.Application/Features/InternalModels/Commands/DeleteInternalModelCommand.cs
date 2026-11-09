using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

public sealed record DeleteInternalModelCommand(EntityIdRequest Dto) : IRequest<Response<OperationResult>>;
