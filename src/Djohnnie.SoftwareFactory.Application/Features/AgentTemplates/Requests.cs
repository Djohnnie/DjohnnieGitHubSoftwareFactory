using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed record GetTemplatesQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<AgentTemplate>>>;
public sealed record GetTemplateQuery(EntityIdRequest Dto) : IRequest<Response<AgentTemplate?>>;
public sealed record SaveTemplateCommand(SaveTemplateRequest Dto) : IRequest<Response<AgentTemplate>>;
public sealed record DeleteTemplateCommand(EntityIdRequest Dto) : IRequest<Response<OperationResult>>;
public sealed record ApplyTemplateCommand(ApplyTemplateRequest Dto, IProgress<AgentTemplateApplyResult>? Progress = null)
    : IRequest<Response<CollectionResult<AgentTemplateApplyResult>>>;
