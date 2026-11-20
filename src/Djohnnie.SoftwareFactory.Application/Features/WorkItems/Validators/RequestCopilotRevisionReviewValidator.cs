using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class RequestCopilotRevisionReviewValidator : AbstractValidator<RequestCopilotRevisionReviewCommand>
{
    public RequestCopilotRevisionReviewValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
