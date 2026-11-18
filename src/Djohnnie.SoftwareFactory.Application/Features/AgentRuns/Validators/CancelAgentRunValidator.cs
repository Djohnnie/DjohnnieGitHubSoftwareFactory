using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

public sealed class CancelAgentRunValidator : AbstractValidator<CancelAgentRunCommand>
{
    public CancelAgentRunValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
