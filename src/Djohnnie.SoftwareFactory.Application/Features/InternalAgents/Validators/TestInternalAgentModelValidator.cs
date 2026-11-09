using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class TestInternalAgentModelValidator : AbstractValidator<TestInternalAgentModelQuery>
{
    public TestInternalAgentModelValidator()
    {
        RuleFor(request => request.Dto.Key).IsInternalAgentKey();
        RuleFor(request => request.Dto.Model)
            .NotEmpty().WithMessage("The model is required.")
            .MaximumLength(InternalModel.MaxNameLength)
            .WithMessage($"The model cannot be longer than {InternalModel.MaxNameLength} characters.");
    }
}
