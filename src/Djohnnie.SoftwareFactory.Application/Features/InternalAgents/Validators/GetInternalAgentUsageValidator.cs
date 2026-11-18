using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class GetInternalAgentUsageValidator : AbstractValidator<GetInternalAgentUsageQuery>
{
    public GetInternalAgentUsageValidator()
    {
        RuleFor(request => request.Dto.Key!).IsInternalAgentKey().When(request => request.Dto.Key is not null);
        RuleFor(request => request.Dto.Page).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Dto.PageSize).InclusiveBetween(1, InternalAgentUsageQuery.MaxPageSize);
    }
}
