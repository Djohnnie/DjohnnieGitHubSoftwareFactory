using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

public sealed class GetRepositoryAgentRunsValidator : AbstractValidator<GetRepositoryAgentRunsQuery>
{
    public GetRepositoryAgentRunsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
