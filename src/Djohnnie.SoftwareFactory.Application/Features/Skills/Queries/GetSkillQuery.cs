using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed record GetSkillQuery(EntityIdRequest Dto) : IRequest<Response<Skill?>>;
