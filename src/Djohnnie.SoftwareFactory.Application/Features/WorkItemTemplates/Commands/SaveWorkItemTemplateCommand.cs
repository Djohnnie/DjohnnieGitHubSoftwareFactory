using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed record SaveWorkItemTemplateCommand(SaveWorkItemTemplateRequest Dto) : IRequest<Response<WorkItemTemplate>>;
