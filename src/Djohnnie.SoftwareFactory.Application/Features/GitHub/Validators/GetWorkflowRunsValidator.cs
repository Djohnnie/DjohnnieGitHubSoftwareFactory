using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetWorkflowRunsValidator : AbstractValidator<GetWorkflowRunsQuery>
{
    public GetWorkflowRunsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.WorkflowId).GreaterThan(0).When(request => request.Dto.WorkflowId is not null);
        RuleFor(request => request.Dto.Page).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Dto.PageSize).InclusiveBetween(1, WorkflowRunsRequest.MaxPageSize);
    }
}
