using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetWorkflowsValidator : AbstractValidator<GetWorkflowsQuery>
{
    public GetWorkflowsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
