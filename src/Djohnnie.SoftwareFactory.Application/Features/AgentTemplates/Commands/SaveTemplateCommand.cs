using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed record SaveTemplateCommand(SaveTemplateRequest Dto) : IRequest<Response<AgentTemplate>>;
