using Djohnnie.SoftwareFactory.Contracts.Skills;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class SaveSkillValidator : AbstractValidator<SaveSkillCommand>
{
    public SaveSkillValidator()
    {
        RuleFor(request => request.Dto.Skill).NotNull();
        When(request => request.Dto.Skill is not null, () =>
        {
            RuleFor(request => request.Dto.Skill.Name).Must(name => Skill.IsValidName(name?.Trim()))
                .WithMessage("A valid skill name is required: lowercase letters, digits and single hyphens, at most 64 characters.");
            RuleFor(request => request.Dto.Skill.Description).NotEmpty().MaximumLength(Skill.MaxDescriptionLength);
            RuleFor(request => request.Dto.Skill.License).MaximumLength(Skill.MaxLicenseLength);
            RuleFor(request => request.Dto.Skill.Compatibility).MaximumLength(Skill.MaxCompatibilityLength);
            RuleFor(request => request.Dto.Skill.AllowedTools).MaximumLength(Skill.MaxAllowedToolsLength);
            RuleFor(request => request.Dto.Skill.Instructions).NotNull().MaximumLength(Skill.MaxInstructionsLength);
        });
    }
}
