using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class DeleteWorkItemTemplateValidator : AbstractValidator<DeleteWorkItemTemplateCommand>
{
    public DeleteWorkItemTemplateValidator()
    {
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
