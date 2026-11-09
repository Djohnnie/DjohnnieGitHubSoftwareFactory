using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GetWorkItemsValidator : AbstractValidator<GetWorkItemsQuery>
{
    public GetWorkItemsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleForEach(request => request.Dto.Types).IsInEnum();
        RuleFor(request => request.Dto.ParentId).NotEmpty().When(request => request.Dto.ParentId is not null);
    }
}
