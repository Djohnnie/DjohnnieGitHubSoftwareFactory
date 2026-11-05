using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GetWorkItemsValidator : AbstractValidator<GetWorkItemsQuery>
{
    public GetWorkItemsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
        RuleForEach(request => request.Dto.Types).IsInEnum();
        RuleFor(request => request.Dto.ParentId).GreaterThan(0).When(request => request.Dto.ParentId is not null);
    }
}
