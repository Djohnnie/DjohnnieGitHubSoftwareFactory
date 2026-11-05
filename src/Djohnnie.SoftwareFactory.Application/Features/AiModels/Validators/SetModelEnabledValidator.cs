using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class SetModelEnabledValidator : AbstractValidator<SetModelEnabledCommand>
{
    public SetModelEnabledValidator()
    {
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
