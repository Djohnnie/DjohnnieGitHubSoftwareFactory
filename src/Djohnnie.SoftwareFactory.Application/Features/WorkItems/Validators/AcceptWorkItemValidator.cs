using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class AcceptWorkItemValidator : AbstractValidator<AcceptWorkItemCommand>
{
    public AcceptWorkItemValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
