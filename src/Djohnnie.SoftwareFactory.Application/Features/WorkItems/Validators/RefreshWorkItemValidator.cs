using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class RefreshWorkItemValidator : AbstractValidator<RefreshWorkItemCommand>
{
    public RefreshWorkItemValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
