using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetFileValidator : AbstractValidator<GetFileQuery>
{
    public GetFileValidator()
    {
        RuleFor(request => request.Dto).NotNull().SetValidator(new RepositoryPathValidator());
    }
}
