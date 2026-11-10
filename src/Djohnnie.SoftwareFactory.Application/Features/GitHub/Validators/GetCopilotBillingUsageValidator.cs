using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetCopilotBillingUsageValidator : AbstractValidator<GetCopilotBillingUsageQuery>
{
    public GetCopilotBillingUsageValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Report).IsInEnum();
    }
}
