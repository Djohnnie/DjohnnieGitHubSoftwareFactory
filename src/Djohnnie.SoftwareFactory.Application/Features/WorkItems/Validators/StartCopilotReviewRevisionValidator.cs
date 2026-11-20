using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class StartCopilotReviewRevisionValidator : AbstractValidator<StartCopilotReviewRevisionCommand>
{
    public StartCopilotReviewRevisionValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
