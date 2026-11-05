using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class GetEpicTemplateValidator : AbstractValidator<GetEpicTemplateQuery>
{
    public GetEpicTemplateValidator()
    {
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
