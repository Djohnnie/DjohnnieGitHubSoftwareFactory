using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class ResetInternalAgentValidator : AbstractValidator<ResetInternalAgentCommand>
{
    public ResetInternalAgentValidator()
    {
        RuleFor(request => request.Dto.Key).IsInternalAgentKey();
    }
}
