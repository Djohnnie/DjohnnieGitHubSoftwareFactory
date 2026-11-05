using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class ScrapeModelsValidator : AbstractValidator<ScrapeModelsQuery>
{
    public ScrapeModelsValidator()
    {
        RuleFor(request => request.Dto.Url).Custom((url, context) =>
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
}
