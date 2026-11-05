using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

/// <summary>Restores the default system prompt; whether the agent is enabled stays as it is.</summary>
public sealed class ResetInternalAgentCommandHandler(InternalAgentRegistry registry, IInternalAgentStore store) : IRequestHandler<ResetInternalAgentCommand, Response<InternalAgent>>
{
    public async Task<Response<InternalAgent>> Handle(ResetInternalAgentCommand request, CancellationToken cancellationToken)
    {
        await registry.EnsureSeededAsync(cancellationToken);
        var agent = await registry.GetAsync(request.Dto.Key, cancellationToken);
        await store.SaveAsync(agent with { Instructions = agent.DefaultInstructions }, cancellationToken);
        return new(await registry.GetAsync(request.Dto.Key, cancellationToken));
    }
}
