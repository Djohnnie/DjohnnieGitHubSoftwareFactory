using System.Collections;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>
/// The environment the Copilot runtime (and so every shell command of the agent) starts with: the worker's own environment
/// without its secrets. The agent approves its own tool calls and reads repository content that may try to talk it into
/// things, so it must not find the database connection string, the Service Bus connection, the worker's GitHub token or
/// the managed identity endpoint in its environment.
/// </summary>
public static class AgentEnvironment
{
    /// <summary>Variables (or prefixes, ending in <c>*</c>) that are never passed on, compared case-insensitively.</summary>
    private static readonly string[] Blocked =
    [
        "ConnectionStrings__*", "ServiceBus*", "AgentWorker__*", "AgentRuns__*", "GitHub__*", "OpenAI__*", "MediatR__*",
        "IDENTITY_*", "MSI_*", "AZURE_*", "CONTAINER_APP_*", "GH_*", "GITHUB_*", "COPILOT_*"
    ];

    /// <summary>Words that make any variable look like a secret.</summary>
    private static readonly string[] SecretWords = ["TOKEN", "SECRET", "PASSWORD", "PASSWD", "APIKEY", "API_KEY", "CONNECTIONSTRING", "CREDENTIAL"];

    public static Dictionary<string, string> Sanitized(IDictionary variables)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in variables)
        {
            if (entry.Key is string name && entry.Value is string value && IsAllowed(name))
            {
                result[name] = value;
            }
        }
        return result;
    }

    public static bool IsAllowed(string name)
    {
        foreach (var blocked in Blocked)
        {
            var matches = blocked.EndsWith('*')
                ? name.StartsWith(blocked[..^1], StringComparison.OrdinalIgnoreCase)
                : name.Equals(blocked, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                return false;
            }
        }
        var normalized = name.Replace("__", "_", StringComparison.Ordinal).ToUpperInvariant();
        return !SecretWords.Any(word => normalized.Contains(word, StringComparison.Ordinal));
    }
}
