using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class ImportModelsValidator : AbstractValidator<ImportModelsCommand>
{
    public ImportModelsValidator()
    {
        RuleFor(request => request.Dto.Models).NotNull();
        RuleForEach(request => request.Dto.Models).NotNull().SetValidator(new ModelValidator());
    }
}
