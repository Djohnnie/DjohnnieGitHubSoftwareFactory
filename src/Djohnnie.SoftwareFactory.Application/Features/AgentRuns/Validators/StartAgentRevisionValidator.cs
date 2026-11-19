using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

public sealed class StartAgentRevisionValidator : AbstractValidator<StartAgentRevisionCommand>
{
    public StartAgentRevisionValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
