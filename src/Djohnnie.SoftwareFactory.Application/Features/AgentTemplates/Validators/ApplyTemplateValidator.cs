using Djohnnie.SoftwareFactory.Contracts.Agents;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

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
