using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class SaveWorkItemTemplateValidator : AbstractValidator<SaveWorkItemTemplateCommand>
{
    public SaveWorkItemTemplateValidator()
    {
        RuleFor(request => request.Dto.Template).NotNull();
        When(request => request.Dto.Template is not null, () =>
        {
            RuleFor(request => request.Dto.Template.Type).IsInEnum();
            RuleFor(request => request.Dto.Template.Name).NotEmpty().MaximumLength(200);
            RuleFor(request => request.Dto.Template.TitleTemplate).NotEmpty().MaximumLength(256);
            RuleFor(request => request.Dto.Template.Description).NotEmpty().MaximumLength(WorkItem.MaxDescriptionLength);
        });
    }
}
