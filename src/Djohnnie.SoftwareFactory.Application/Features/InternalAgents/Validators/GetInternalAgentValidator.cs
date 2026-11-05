using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class GetInternalAgentValidator : AbstractValidator<GetInternalAgentQuery>
{
    public GetInternalAgentValidator()
    {
        RuleFor(request => request.Dto.Key).IsInternalAgentKey();
    }
}
