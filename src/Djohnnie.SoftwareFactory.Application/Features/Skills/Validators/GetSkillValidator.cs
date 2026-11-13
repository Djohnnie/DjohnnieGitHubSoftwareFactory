using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class GetSkillValidator : AbstractValidator<GetSkillQuery>
{
    public GetSkillValidator()
    {
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
