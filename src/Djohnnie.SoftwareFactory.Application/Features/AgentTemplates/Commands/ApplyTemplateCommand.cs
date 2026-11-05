using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed record ApplyTemplateCommand(ApplyTemplateRequest Dto, IProgress<AgentTemplateApplyResult>? Progress = null)
    : IRequest<Response<CollectionResult<AgentTemplateApplyResult>>>;
