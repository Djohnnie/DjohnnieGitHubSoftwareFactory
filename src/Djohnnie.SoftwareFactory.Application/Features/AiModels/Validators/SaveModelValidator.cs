using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class SaveModelValidator : AbstractValidator<SaveModelCommand>
{
    public SaveModelValidator()
    {
        RuleFor(request => request.Dto).NotNull().SetValidator(new ModelValidator());
    }
}
