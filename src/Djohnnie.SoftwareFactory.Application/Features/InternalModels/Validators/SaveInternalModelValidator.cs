using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

public sealed class SaveInternalModelValidator : AbstractValidator<SaveInternalModelCommand>
{
    public SaveInternalModelValidator()
    {
        RuleFor(request => request.Dto.Name)
            .NotEmpty().WithMessage("The model name is required.")
            .MaximumLength(InternalModel.MaxNameLength)
            .WithMessage($"The model name cannot be longer than {InternalModel.MaxNameLength} characters.");
        RuleFor(request => request.Dto.Label)
            .NotEmpty().WithMessage("The label is required.")
            .MaximumLength(InternalModel.MaxLabelLength)
            .WithMessage($"The label cannot be longer than {InternalModel.MaxLabelLength} characters.");
        RuleFor(request => request.Dto.Description)
            .MaximumLength(InternalModel.MaxDescriptionLength)
            .WithMessage($"The description cannot be longer than {InternalModel.MaxDescriptionLength} characters.");
        RuleFor(request => request.Dto.InputPricePerMillion)
            .InclusiveBetween(0, InternalModel.MaxPrice).WithMessage("The input price must be between 0 and 1,000,000.");
        RuleFor(request => request.Dto.OutputPricePerMillion)
            .InclusiveBetween(0, InternalModel.MaxPrice).WithMessage("The output price must be between 0 and 1,000,000.");
        RuleFor(request => request.Dto.CachedInputPricePerMillion)
            .InclusiveBetween(0m, InternalModel.MaxPrice).WithMessage("The cached input price must be between 0 and 1,000,000.");
    }
}
