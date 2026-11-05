using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class SaveModelValidator : AbstractValidator<SaveModelCommand>
{
    public SaveModelValidator() => RuleFor(request => request.Dto).NotNull().SetValidator(new ModelValidator());
}

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

public sealed class ImportModelsValidator : AbstractValidator<ImportModelsCommand>
{
    public ImportModelsValidator()
    {
        RuleFor(request => request.Dto.Models).NotNull();
        RuleForEach(request => request.Dto.Models).NotNull().SetValidator(new ModelValidator());
    }
}

public sealed class ScrapeModelsValidator : AbstractValidator<ScrapeModelsQuery>
{
    public ScrapeModelsValidator() => RuleFor(request => request.Dto.Url).Custom((url, context) =>
    {
        try
        {
            ModelPricingScraper.ValidateUrl(url);
        }
        catch (ArgumentException ex)
        {
            context.AddFailure(ex.Message);
        }
    });
}

public sealed class SetModelEnabledValidator : AbstractValidator<SetModelEnabledCommand>
{
    public SetModelEnabledValidator() => RuleFor(request => request.Dto.Id).GreaterThan(0);
}

public sealed class DeleteModelValidator : AbstractValidator<DeleteModelCommand>
{
    public DeleteModelValidator() => RuleFor(request => request.Dto.Id).GreaterThan(0);
}

public sealed class SetModelFavoriteValidator : AbstractValidator<SetModelFavoriteCommand>
{
    public SetModelFavoriteValidator() => RuleFor(request => request.Dto.Id).GreaterThan(0);
}
