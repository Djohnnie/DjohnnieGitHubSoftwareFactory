using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class SaveInternalAgentValidator : AbstractValidator<SaveInternalAgentCommand>
{
    public SaveInternalAgentValidator()
    {
        RuleFor(request => request.Dto.Agent).NotNull();
        When(request => request.Dto.Agent is not null, () =>
        {
            RuleFor(request => request.Dto.Agent.Key).IsInternalAgentKey();
            RuleFor(request => request.Dto.Agent.Instructions)
                .NotEmpty().WithMessage("The system prompt is required.")
                .MaximumLength(InternalAgent.MaxInstructionsLength)
                .WithMessage($"The system prompt cannot be longer than {InternalAgent.MaxInstructionsLength} characters.");
        });
    }
}
