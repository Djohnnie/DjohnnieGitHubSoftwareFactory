using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed record DeleteEpicTemplateCommand(EntityIdRequest Dto) : IRequest<Response<OperationResult>>;
