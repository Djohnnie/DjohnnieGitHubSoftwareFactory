using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class EnableSkillValidator : AbstractValidator<EnableSkillCommand>
{
    public EnableSkillValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.SkillId).NotEmpty();
    }
}
