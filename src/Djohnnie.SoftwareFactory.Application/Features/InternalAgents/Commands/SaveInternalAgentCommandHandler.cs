using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

/// <summary>Saves the system prompt and whether the agent is enabled; the other fields belong to the code.</summary>
public sealed class SaveInternalAgentCommandHandler(InternalAgentRegistry registry, IInternalAgentStore store) : IRequestHandler<SaveInternalAgentCommand, Response<InternalAgent>>
{
    public async Task<Response<InternalAgent>> Handle(SaveInternalAgentCommand request, CancellationToken cancellationToken)
    {
        await registry.EnsureSeededAsync(cancellationToken);
        await store.SaveAsync(request.Dto.Agent, cancellationToken);
        return new(await registry.GetAsync(request.Dto.Agent.Key, cancellationToken));
    }
}
