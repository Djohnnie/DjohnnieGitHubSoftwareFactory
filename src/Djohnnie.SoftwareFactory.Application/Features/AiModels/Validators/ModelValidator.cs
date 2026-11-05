using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class ModelValidator : AbstractValidator<Djohnnie.SoftwareFactory.Contracts.AiModels.AiModel>
{
    public ModelValidator()
    {
        RuleFor(model => model.Id).GreaterThanOrEqualTo(0);
        RuleFor(model => model.Name).NotEmpty().MaximumLength(100);
        RuleFor(model => model.ModelId).NotEmpty().MaximumLength(100);
        RuleFor(model => model.Provider).MaximumLength(100);
        RuleFor(model => model.ReleaseStatus).MaximumLength(50);
        RuleFor(model => model.Category).MaximumLength(50);
        RuleFor(model => model.Tier).MaximumLength(50);
        RuleFor(model => model.Threshold).MaximumLength(50);
        RuleFor(model => model.SourceUrl).MaximumLength(500);
    }
}
