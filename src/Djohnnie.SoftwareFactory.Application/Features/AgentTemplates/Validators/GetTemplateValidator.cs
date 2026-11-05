using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class GetTemplateValidator : AbstractValidator<GetTemplateQuery>
{
    public GetTemplateValidator()
    {
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
