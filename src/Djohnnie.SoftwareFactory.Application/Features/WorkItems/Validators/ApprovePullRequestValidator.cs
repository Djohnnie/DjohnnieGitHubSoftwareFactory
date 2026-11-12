using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class ApprovePullRequestValidator : AbstractValidator<ApprovePullRequestCommand>
{
    public ApprovePullRequestValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
        RuleFor(request => request.Dto.Body).MaximumLength(PullRequestApproval.MaxBodyLength);
    }
}
