using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed record SetModelEnabledCommand(SetModelEnabledRequest Dto) : IRequest<Response<OperationResult>>;
