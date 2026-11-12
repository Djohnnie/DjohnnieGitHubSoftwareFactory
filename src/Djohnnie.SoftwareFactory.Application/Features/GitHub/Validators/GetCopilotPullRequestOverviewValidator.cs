using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetCopilotPullRequestOverviewValidator : AbstractValidator<GetCopilotPullRequestOverviewQuery>
{
    public GetCopilotPullRequestOverviewValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
