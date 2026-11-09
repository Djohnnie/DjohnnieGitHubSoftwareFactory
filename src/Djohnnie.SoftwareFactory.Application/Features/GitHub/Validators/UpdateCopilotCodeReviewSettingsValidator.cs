using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class UpdateCopilotCodeReviewSettingsValidator : AbstractValidator<UpdateCopilotCodeReviewSettingsCommand>
{
    public UpdateCopilotCodeReviewSettingsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
