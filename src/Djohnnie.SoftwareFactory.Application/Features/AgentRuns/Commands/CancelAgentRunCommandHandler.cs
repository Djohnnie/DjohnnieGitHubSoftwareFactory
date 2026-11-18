using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <remarks>A worker that is running the agent notices at its next lease renewal, stops the agent and pushes nothing.</remarks>
public sealed class CancelAgentRunCommandHandler(IAgentRunStore store) : IRequestHandler<CancelAgentRunCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(CancelAgentRunCommand request, CancellationToken cancellationToken)
    {
        if (!await store.CancelAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken))
        {
            throw new DomainRuleException("The run has already ended.");
        }
        return new(new OperationResult());
    }
}
