using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class SaveWorkItemValidator : AbstractValidator<SaveWorkItemCommand>
{
    public SaveWorkItemValidator()
    {
        RuleFor(request => request.Dto).NotNull().SetValidator(new ContractValidator<WorkItem>());
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Type).IsInEnum();
        RuleFor(request => request.Dto.ParentId).NotEmpty().When(request => request.Dto.ParentId is not null);
        RuleFor(request => request.Dto.ParentId).NotNull().When(request => request.Dto.Type.RequiresParent)
            .WithMessage(request => $"A {request.Dto.Type.DisplayName.ToLowerInvariant()} needs a parent.");
    }
}
