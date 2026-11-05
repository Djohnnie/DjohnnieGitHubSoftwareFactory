using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed record GetEpicTemplateQuery(EntityIdRequest Dto) : IRequest<Response<EpicTemplate?>>;
