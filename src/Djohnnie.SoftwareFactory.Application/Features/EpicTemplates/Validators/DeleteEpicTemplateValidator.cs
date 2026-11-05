using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class DeleteEpicTemplateValidator : AbstractValidator<DeleteEpicTemplateCommand>
{
    public DeleteEpicTemplateValidator()
    {
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
