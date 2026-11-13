using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class DeleteSkillValidator : AbstractValidator<DeleteSkillCommand>
{
    public DeleteSkillValidator()
    {
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
