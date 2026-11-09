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
/// <param name="chatClient">The client of the configured model.</param>
/// <param name="loggerFactory">Creates the loggers of the agents.</param>
/// <param name="modelClientFactory">Creates a client for another model on the same endpoint.</param>
/// <param name="defaultModel">The name of the configured model.</param>
public sealed class AssistantAgentFactory(
    IChatClient? chatClient,
    ILoggerFactory loggerFactory,
    Func<string, IChatClient>? modelClientFactory = null,
    string? defaultModel = null)
{
    public const string AgentName = "DGHSF Copilot";

    /// <summary><c>false</c> when the OpenAI model or key is missing.</summary>
    public bool IsConfigured => chatClient is not null;

    /// <summary>The configured model, used by agents without a model of their own.</summary>
    public string? DefaultModel => defaultModel?.Trim();

    public static AssistantAgentFactory Create(OpenAIOptions options, ILoggerFactory loggerFactory)
    {
        return new(CreateChatClient(options), loggerFactory, model => CreateChatClient(options.ForModel(model))!, options.Model);
    }

    /// <param name="name">The name of the agent.</param>
    /// <param name="instructions">The system prompt.</param>
    /// <param name="tools">The tools of the agent.</param>
    /// <param name="model">The model to use; empty for the configured model.</param>
    public AssistantConversation CreateConversation(string name, string instructions, AssistantTools? tools = null, string? model = null)
    {
        return new(CreateAgent(name, instructions, tools?.Tools, model));
    }

    /// <param name="name">The name of the agent.</param>
    /// <param name="instructions">The system prompt.</param>
    /// <param name="tools">The tools of the agent.</param>
    /// <param name="model">The model to use; empty for the configured model.</param>
    public AIAgent CreateAgent(string name, string instructions, IList<AITool>? tools = null, string? model = null)
    {
        return new ChatClientAgent(ClientFor(model), instructions, name, tools: tools, loggerFactory: loggerFactory);
    }

    /// <summary>Sends a short message to the model; throws when the model can't be used.</summary>
    public async Task TestModelAsync(string model, CancellationToken cancellationToken = default)
    {
        await ClientFor(model).GetResponseAsync("Reply with the single word OK.", cancellationToken: cancellationToken);
    }

    private IChatClient ClientFor(string? model)
    {
        if (chatClient is null)
        {
            throw new InvalidOperationException(
                $"DGHSF Copilot is not configured. Set {OpenAIOptions.SectionName}:Model and {OpenAIOptions.SectionName}:ApiKey (and optionally {OpenAIOptions.SectionName}:Endpoint).");
        }

        return string.IsNullOrWhiteSpace(model) || modelClientFactory is null ? chatClient : modelClientFactory(model.Trim());
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
