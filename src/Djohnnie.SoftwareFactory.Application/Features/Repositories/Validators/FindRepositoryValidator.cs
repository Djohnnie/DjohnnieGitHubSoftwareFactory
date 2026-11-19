using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class FindRepositoryValidator : AbstractValidator<FindRepositoryQuery>
{
    public FindRepositoryValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
