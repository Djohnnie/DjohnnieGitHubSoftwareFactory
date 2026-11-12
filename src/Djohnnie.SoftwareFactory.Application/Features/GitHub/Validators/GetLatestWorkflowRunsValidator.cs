using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetLatestWorkflowRunsValidator : AbstractValidator<GetLatestWorkflowRunsQuery>
{
    public GetLatestWorkflowRunsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
