using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GenerateChildWorkItemsValidator : AbstractValidator<GenerateChildWorkItemsQuery>
{
    public GenerateChildWorkItemsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.ParentId).NotEmpty();
    }
}
