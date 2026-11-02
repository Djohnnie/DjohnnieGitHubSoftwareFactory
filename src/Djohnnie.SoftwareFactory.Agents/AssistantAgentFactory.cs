using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Creates DGHSF Copilot agents (Microsoft Agent Framework) on top of the configured OpenAI-compatible model.
/// Every agent shares the generic DGHSF Copilot instructions; pages add their own task instructions and tools.
/// </summary>
public sealed class AssistantAgentFactory(IChatClient? chatClient, ILoggerFactory loggerFactory)
{
    public const string AgentName = "DGHSF Copilot";

    private const string BaseInstructions = """
        You are DGHSF Copilot, the assistant built into DGHSF (Djohnnie's GitHub Software Factory).
        DGHSF is a personal web app to manage GitHub Copilot custom agents per repository and to plan work (epics)
        that is turned into GitHub issues and handed to the Copilot cloud agent.
        Be concise and practical. Use Markdown for formatting. Never claim that something was saved, committed or
        created on GitHub: the user always reviews the form and saves it themselves.
        """;

    /// <summary><c>false</c> when the OpenAI model or key is missing.</summary>
    public bool IsConfigured => chatClient is not null;

    public AssistantConversation CreateConversation(string instructions, AssistantTools? tools = null) =>
        new(CreateAgent(instructions, tools?.Tools));

    public AIAgent CreateAgent(string instructions, IList<AITool>? tools = null)
    {
        if (chatClient is null)
        {
            throw new InvalidOperationException(
                $"DGHSF Copilot is not configured. Set {OpenAIOptions.SectionName}:Model and {OpenAIOptions.SectionName}:ApiKey (and optionally {OpenAIOptions.SectionName}:Endpoint).");
        }

        return new ChatClientAgent(chatClient, $"{BaseInstructions}\n\n{instructions}", AgentName, tools: tools, loggerFactory: loggerFactory);
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
