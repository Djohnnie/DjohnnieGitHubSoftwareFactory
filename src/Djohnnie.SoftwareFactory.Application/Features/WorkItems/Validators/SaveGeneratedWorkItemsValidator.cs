using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class SaveGeneratedWorkItemsValidator : AbstractValidator<SaveGeneratedWorkItemsCommand>
{
    public SaveGeneratedWorkItemsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.ParentId).NotEmpty();
        RuleFor(request => request.Dto.Items).NotEmpty().WithMessage("Select at least one item to save.")
            .Must(items => items.Count <= GeneratedWorkItem.MaxPerRun).WithMessage($"At most {GeneratedWorkItem.MaxPerRun} items can be saved at once.");
        RuleForEach(request => request.Dto.Items).SetValidator(new ContractValidator<GeneratedWorkItem>());
    }
}
