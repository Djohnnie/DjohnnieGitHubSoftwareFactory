using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetCopilotPullRequestActivityValidator : AbstractValidator<GetCopilotPullRequestActivityQuery>
{
    public GetCopilotPullRequestActivityValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
