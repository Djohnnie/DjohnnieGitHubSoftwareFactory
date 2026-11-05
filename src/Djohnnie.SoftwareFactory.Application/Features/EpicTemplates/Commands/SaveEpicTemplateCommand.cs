using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed record SaveEpicTemplateCommand(SaveEpicTemplateRequest Dto) : IRequest<Response<EpicTemplate>>;
