using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetCopilotCodeReviewSettingsValidator : AbstractValidator<GetCopilotCodeReviewSettingsQuery>
{
    public GetCopilotCodeReviewSettingsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
