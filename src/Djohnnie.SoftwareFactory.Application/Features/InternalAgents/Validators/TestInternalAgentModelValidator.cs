using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class TestInternalAgentModelValidator : AbstractValidator<TestInternalAgentModelQuery>
{
    public TestInternalAgentModelValidator()
    {
        RuleFor(request => request.Dto.Model)
            .NotEmpty().WithMessage("The model is required.")
            .MaximumLength(InternalAgent.MaxModelLength)
            .WithMessage($"The model cannot be longer than {InternalAgent.MaxModelLength} characters.");
    }
}
