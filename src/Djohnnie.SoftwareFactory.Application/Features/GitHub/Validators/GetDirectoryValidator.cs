using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetDirectoryValidator : AbstractValidator<GetDirectoryQuery>
{
    public GetDirectoryValidator()
    {
        RuleFor(request => request.Dto).NotNull().SetValidator(new RepositoryPathValidator());
    }
}
