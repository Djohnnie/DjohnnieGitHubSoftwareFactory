using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetRepositoryValidator : AbstractValidator<GetRepositoryQuery>
{
    public GetRepositoryValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200)
            .Matches(@"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$");
    }
}
