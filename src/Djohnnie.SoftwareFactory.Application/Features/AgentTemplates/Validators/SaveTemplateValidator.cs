using Djohnnie.SoftwareFactory.Contracts.Agents;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class SaveTemplateValidator : AbstractValidator<SaveTemplateCommand>
{
    public SaveTemplateValidator()
    {
        RuleFor(request => request.Dto.FileName).Must(name => AgentDefinition.IsValidFileName(name?.Trim()))
            .WithMessage("A valid agent file name is required.").MaximumLength(100);
        RuleFor(request => request.Dto.Agent).NotNull();
        When(request => request.Dto.Agent is not null, () =>
        {
            RuleFor(request => request.Dto.Agent.Name).MaximumLength(200);
            RuleFor(request => request.Dto.Agent.Prompt).NotNull().MaximumLength(AgentDefinition.MaxPromptLength);
        });
    }
}
