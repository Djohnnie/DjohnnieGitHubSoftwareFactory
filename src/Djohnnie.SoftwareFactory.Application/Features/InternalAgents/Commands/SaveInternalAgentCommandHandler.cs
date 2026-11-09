using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

/// <summary>Saves the system prompt, the model of a Copilot and whether the agent is enabled; the other fields belong to the code.</summary>
public sealed class SaveInternalAgentCommandHandler(InternalAgentRegistry registry, IInternalAgentStore store) : IRequestHandler<SaveInternalAgentCommand, Response<InternalAgent>>
{
    public async Task<Response<InternalAgent>> Handle(SaveInternalAgentCommand request, CancellationToken cancellationToken)
    {
        await registry.EnsureSeededAsync(cancellationToken);
        // A Copilot that never had a model may still be toggled from the overview; one that has a model keeps one.
        var current = await registry.GetAsync(request.Dto.Agent.Key, cancellationToken);
        if (current.Kind == InternalAgentKind.Copilot && !string.IsNullOrWhiteSpace(current.Model) && string.IsNullOrWhiteSpace(request.Dto.Agent.Model))
        {
            throw new DomainRuleException("The model is required.");
        }

        await store.SaveAsync(request.Dto.Agent, cancellationToken);
        return new(await registry.GetAsync(request.Dto.Agent.Key, cancellationToken));
    }
}
