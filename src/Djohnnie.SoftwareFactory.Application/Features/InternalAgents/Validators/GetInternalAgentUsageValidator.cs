using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class GetInternalAgentUsageValidator : AbstractValidator<GetInternalAgentUsageQuery>
{
    public GetInternalAgentUsageValidator()
    {
        RuleFor(request => request.Dto.Key).IsInternalAgentKey();
    }
}
