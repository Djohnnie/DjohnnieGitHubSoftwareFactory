using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class AssignWorkItemAgentValidator : AbstractValidator<AssignWorkItemAgentCommand>
{
    public AssignWorkItemAgentValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
