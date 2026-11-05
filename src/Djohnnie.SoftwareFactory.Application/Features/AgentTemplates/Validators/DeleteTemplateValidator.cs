using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class DeleteTemplateValidator : AbstractValidator<DeleteTemplateCommand>
{
    public DeleteTemplateValidator()
    {
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
