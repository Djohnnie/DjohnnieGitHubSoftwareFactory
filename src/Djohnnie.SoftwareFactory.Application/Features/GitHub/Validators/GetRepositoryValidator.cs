using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetRepositoryValidator : AbstractValidator<GetRepositoryQuery>
{
    public GetRepositoryValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
