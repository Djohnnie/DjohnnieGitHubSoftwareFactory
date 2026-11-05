using Djohnnie.SoftwareFactory.Contracts.GitHub;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class RepositoryPathValidator : AbstractValidator<RepositoryPathRequest>
{
    public RepositoryPathValidator()
    {
        RuleFor(request => request.RepositoryFullName).NotEmpty();
        RuleFor(request => request.Path).NotEmpty();
    }
}
