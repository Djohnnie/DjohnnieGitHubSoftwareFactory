using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed record DeleteTemplateCommand(EntityIdRequest Dto) : IRequest<Response<OperationResult>>;
