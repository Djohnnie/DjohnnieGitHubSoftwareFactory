using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>
/// The Service Bus queue that carries agent run ids from the Tasks function app to the worker's Container Apps job. Both read
/// the connection like the Functions Service Bus extension does: <c>ServiceBus</c> as a connection string, or
/// <c>ServiceBus__fullyQualifiedNamespace</c> (plus <c>ServiceBus__clientId</c> for a user-assigned identity) for a managed identity.
/// </summary>
public static class AgentRunQueue
{
    public const string ConnectionName = "ServiceBus";

    /// <summary>The client for the configured connection, or <c>null</c> when none is configured.</summary>
    public static ServiceBusClient? CreateClient(IConfiguration configuration)
    {
        if (configuration[ConnectionName] is { Length: > 0 } connectionString)
        {
            return new ServiceBusClient(connectionString);
        }

        var section = configuration.GetSection(ConnectionName);
        if (section["fullyQualifiedNamespace"] is not { Length: > 0 } fullyQualifiedNamespace)
        {
            return null;
        }

        var credential = section["clientId"] is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : (Azure.Core.TokenCredential)new DefaultAzureCredential();
        return new ServiceBusClient(fullyQualifiedNamespace, credential);
    }

    /// <summary>The message for a run: its id, which also deduplicates the message when the queue has duplicate detection on.</summary>
    public static ServiceBusMessage Message(Guid runId)
    {
        return new ServiceBusMessage(runId.ToString()) { MessageId = runId.ToString(), ContentType = "text/plain" };
    }

    /// <summary>The run id in a message, or <c>null</c> when it isn't one (also accepts a JSON string).</summary>
    public static Guid? RunId(string body)
    {
        return Guid.TryParse(body.Trim().Trim('"'), out var id) ? id : null;
    }
}
