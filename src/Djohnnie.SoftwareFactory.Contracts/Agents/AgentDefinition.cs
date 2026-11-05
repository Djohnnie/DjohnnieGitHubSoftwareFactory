namespace Djohnnie.SoftwareFactory.Contracts.Agents;

public enum AgentToolsMode
{
    /// <summary>All available tools (the <c>tools</c> key is omitted, or <c>["*"]</c>).</summary>
    All,

    /// <summary>No tools at all (<c>tools: []</c>).</summary>
    None,

    /// <summary>Only the tools listed in <see cref="AgentDefinition.Tools"/>.</summary>
    Selected
}

public sealed record AgentHandoff
{
    public string Label { get; set; } = string.Empty;
    public string Agent { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool Send { get; set; }

    /// <summary>The original YAML of this handoff, so unknown keys survive an edit.</summary>
    internal string? Original { get; set; }
}

public sealed record AgentMetadataEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A GitHub Copilot custom agent profile (<c>.github/agents/*.agent.md</c>): YAML frontmatter plus a Markdown prompt.
/// See https://docs.github.com/en/copilot/reference/custom-agents-configuration.
/// </summary>
public sealed record AgentDefinition
{
    public const string Directory = ".github/agents";
    public const string FileExtension = ".agent.md";
    public const int MaxPromptLength = 30_000;

    public string? Name { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary><c>vscode</c>, <c>github-copilot</c>, or <c>null</c> for both.</summary>
    public string? Target { get; set; }

    public string? Model { get; set; }
    public AgentToolsMode ToolsMode { get; set; } = AgentToolsMode.All;
    public List<string> Tools { get; set; } = [];
    public bool DisableModelInvocation { get; set; }
    public bool UserInvocable { get; set; } = true;

    /// <summary>VS Code only; ignored by Copilot on GitHub.com.</summary>
    public string? ArgumentHint { get; set; }

    /// <summary>VS Code only; ignored by Copilot on GitHub.com.</summary>
    public List<AgentHandoff> Handoffs { get; set; } = [];

    public List<AgentMetadataEntry> Metadata { get; set; } = [];

    /// <summary>The <c>mcp-servers</c> object, edited as raw YAML.</summary>
    public string McpServersYaml { get; set; } = string.Empty;

    /// <summary>The Markdown instructions below the frontmatter.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>The frontmatter as it was read, so unknown keys (and their order) survive an edit.</summary>
    internal string? OriginalFrontmatter { get; set; }

    internal string? OriginalMcpServersYaml { get; set; }

    /// <summary>Frontmatter keys this app does not edit; they are written back unchanged.</summary>
    public IReadOnlyList<string> PreservedKeys { get; internal set; } = [];

    public static string GetPath(string fileName)
    {
        return $"{Directory}/{fileName}{FileExtension}";
    }

    /// <summary>Gets the agent's file name without directory and <c>.agent.md</c>/<c>.md</c> extension.</summary>
    public static string GetFileName(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return name[..^FileExtension.Length];
        }

        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }

    /// <summary>Turns a display name into a file-name friendly slug.</summary>
    public static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' ? c : '-')
            .ToArray();

        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-', '.');
    }

    public static bool IsValidFileName(string? value)
    {
        return !string.IsNullOrEmpty(value)
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            && !value.StartsWith('.')
            && !value.EndsWith('.');
    }
}
