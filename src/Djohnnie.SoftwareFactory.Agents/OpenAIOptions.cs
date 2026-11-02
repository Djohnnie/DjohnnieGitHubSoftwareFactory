namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// The OpenAI-compatible endpoint DGHSF Copilot talks to. Works with OpenAI, Azure OpenAI / Microsoft Foundry
/// (Azure resource-root URLs automatically use <c>/openai/v1/</c>) and other compatible services.
/// </summary>
public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    /// <summary>The API base URL or Azure OpenAI/Foundry resource root. Empty for the public OpenAI API.</summary>
    public string? Endpoint { get; set; }

    /// <summary>The model (or Azure deployment) name, e.g. <c>gpt-4.1</c>.</summary>
    public string? Model { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>Use Responses to support reasoning with function tools. Set false for Chat Completions-only services.</summary>
    public bool UseResponsesApi { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(ApiKey);
}
