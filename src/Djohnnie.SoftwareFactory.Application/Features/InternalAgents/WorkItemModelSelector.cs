using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

/// <summary>Lets the Model Selector agent pick a model for a story, bug or task based on its complexity.</summary>
public sealed class WorkItemModelSelector(
    InternalAgentRegistry registry,
    BackgroundAgents agents,
    IAiModelStore models,
    ILogger<WorkItemModelSelector> logger)
{
    /// <summary>
    /// Picks one of the enabled managed models; <c>null</c> when the item isn't assigned to an agent, the Model Selector
    /// is disabled or not configured, there are no enabled models, or the agent failed.
    /// </summary>
    public async Task<ModelSelection?> TrySelectAsync(WorkItem item, CancellationToken cancellationToken = default)
    {
        if (!item.Type.CanBeAssigned || !agents.IsConfigured)
        {
            return null;
        }

        try
        {
            var agent = await registry.GetAsync(InternalAgentKeys.ModelSelector, cancellationToken);
            if (!agent.IsEnabled)
            {
                return null;
            }

            var enabled = await models.GetEnabledAsync(cancellationToken);
            if (enabled.Count == 0)
            {
                return null;
            }

            return await agents.SelectModelAsync(agent, item, enabled, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The Model Selector could not pick a model for work item {Id}.", item.Id);
            return null;
        }
    }
}
