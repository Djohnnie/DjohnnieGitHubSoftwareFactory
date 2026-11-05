using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class SelectWorkItemModelValidator : AbstractValidator<SelectWorkItemModelQuery>
{
    public SelectWorkItemModelValidator()
    {
        RuleFor(request => request.Dto.WorkItem).NotNull();
        When(request => request.Dto.WorkItem is not null, () =>
        {
            RuleFor(request => request.Dto.WorkItem.Type).IsInEnum()
                .Must(type => type.CanBeAssigned).WithMessage("Only user stories, bugs and tasks get a model.");
            RuleFor(request => request.Dto.WorkItem.Description).NotEmpty().WithMessage("Describe the work item before picking a model.");
        });
    }
}
