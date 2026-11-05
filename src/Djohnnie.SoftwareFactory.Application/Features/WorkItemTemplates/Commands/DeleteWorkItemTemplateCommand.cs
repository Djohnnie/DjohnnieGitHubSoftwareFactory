using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed record DeleteWorkItemTemplateCommand(EntityIdRequest Dto) : IRequest<Response<OperationResult>>;
