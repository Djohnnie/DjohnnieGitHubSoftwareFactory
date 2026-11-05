using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Creates DGHSF's internal agents (Microsoft Agent Framework) on top of the configured OpenAI-compatible model.
/// The system prompts come from the internal agents in the database (defaults in <see cref="InternalAgentDefaults"/>).
/// </summary>
public sealed class AssistantAgentFactory(IChatClient? chatClient, ILoggerFactory loggerFactory)
{
    public const string AgentName = "DGHSF Copilot";

    /// <summary><c>false</c> when the OpenAI model or key is missing.</summary>
    public bool IsConfigured => chatClient is not null;

    public AssistantConversation CreateConversation(string name, string instructions, AssistantTools? tools = null)
    {
        return new(CreateAgent(name, instructions, tools?.Tools));
    }

    public AIAgent CreateAgent(string name, string instructions, IList<AITool>? tools = null)
    {
        if (chatClient is null)
        {
            throw new InvalidOperationException(
                $"DGHSF Copilot is not configured. Set {OpenAIOptions.SectionName}:Model and {OpenAIOptions.SectionName}:ApiKey (and optionally {OpenAIOptions.SectionName}:Endpoint).");
        }

        return new ChatClientAgent(chatClient, instructions, name, tools: tools, loggerFactory: loggerFactory);
    }

    /// <summary>Creates the chat client for the configured endpoint, or <c>null</c> when it is not configured.</summary>
    public static IChatClient? CreateChatClient(OpenAIOptions options)
    {
        if (!options.IsConfigured)
        {
            return null;
        }

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            var endpoint = new Uri(options.Endpoint.Trim());
            if (endpoint.AbsolutePath == "/" &&
                (endpoint.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase) ||
                 endpoint.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)))
            {
                endpoint = new UriBuilder(endpoint) { Path = "/openai/v1/" }.Uri;
            }
            clientOptions.Endpoint = endpoint;
        }

        var client = new OpenAIClient(new ApiKeyCredential(options.ApiKey!.Trim()), clientOptions);
#pragma warning disable OPENAI001 // The pinned SDK marks its Responses adapter as experimental.
        return options.UseResponsesApi
            ? client.GetResponsesClient().AsIChatClient(options.Model!.Trim())
            : client.GetChatClient(options.Model!.Trim()).AsIChatClient();
#pragma warning restore OPENAI001
    }
}
