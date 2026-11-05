using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class AssignWorkItemAgentValidator : AbstractValidator<AssignWorkItemAgentCommand>
{
    public AssignWorkItemAgentValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
