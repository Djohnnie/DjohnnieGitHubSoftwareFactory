using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class GetWorkItemCountsValidator : AbstractValidator<GetWorkItemCountsQuery>
{
    public GetWorkItemCountsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
