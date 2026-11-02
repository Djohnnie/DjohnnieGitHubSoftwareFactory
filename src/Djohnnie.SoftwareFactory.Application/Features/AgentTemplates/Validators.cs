using Djohnnie.SoftwareFactory.Contracts.Agents;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class SaveTemplateValidator : AbstractValidator<SaveTemplateCommand>
{
    public SaveTemplateValidator()
    {
        RuleFor(request => request.Dto.Id).GreaterThanOrEqualTo(0);
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

public sealed class ApplyTemplateValidator : AbstractValidator<ApplyTemplateCommand>
{
    public ApplyTemplateValidator()
    {
        RuleFor(request => request.Dto.Template).NotNull();
        RuleFor(request => request.Dto.FileName).Must(name => AgentDefinition.IsValidFileName(name?.Trim()))
            .WithMessage("A valid agent file name is required.");
        RuleFor(request => request.Dto.Repositories).NotNull();
        RuleForEach(request => request.Dto.Repositories).NotEmpty();
    }
}

public sealed class GetTemplateValidator : AbstractValidator<GetTemplateQuery>
{
    public GetTemplateValidator() => RuleFor(request => request.Dto.Id).GreaterThan(0);
}

public sealed class DeleteTemplateValidator : AbstractValidator<DeleteTemplateCommand>
{
    public DeleteTemplateValidator() => RuleFor(request => request.Dto.Id).GreaterThan(0);
}
