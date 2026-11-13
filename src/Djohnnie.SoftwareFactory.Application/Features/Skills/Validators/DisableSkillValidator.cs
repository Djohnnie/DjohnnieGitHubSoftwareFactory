using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class DisableSkillValidator : AbstractValidator<DisableSkillCommand>
{
    public DisableSkillValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.SkillId).NotEmpty();
    }
}
