using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetDirectoryValidator : AbstractValidator<GetDirectoryQuery>
{
    public GetDirectoryValidator()
    {
        RuleFor(request => request.Dto).NotNull().ChildRules(dto =>
        {
            dto.RuleFor(d => d.RepositoryFullName).ValidRepositoryFullName();

            // An empty path lists the root of the repository.
            dto.RuleFor(d => d.Path).NotNull();
        });
    }
}
