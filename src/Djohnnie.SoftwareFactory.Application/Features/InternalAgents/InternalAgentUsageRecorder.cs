using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

/// <summary>Stores the usage of the internal agents. A singleton like the background agents, so each request gets its own scope for the store.</summary>
public sealed class InternalAgentUsageRecorder(IServiceScopeFactory scopes, AssistantAgentFactory factory, ILogger<InternalAgentUsageRecorder> logger) : IAgentUsageRecorder
{
    public async Task RecordAsync(InternalAgent agent, ModelUsage usage, InternalAgentUsageSource source)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var model = string.IsNullOrWhiteSpace(agent.Model) ? factory.DefaultModel ?? string.Empty : agent.Model;
            // Not the caller's token: when the turn is being stopped, the tokens were still paid for.
            await scope.ServiceProvider.GetRequiredService<IInternalAgentStore>().RecordUsageAsync(agent.Key, model, usage, source, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record the usage of {Agent} ({InputTokens} input and {OutputTokens} output tokens).", agent.Key, usage.InputTokens, usage.OutputTokens);
        }
    }
}
