namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>Settings for the GitHub MCP server that gives the Copilots read-only access to the selected repository.</summary>
public sealed class GitHubMcpOptions
{
    public const string SectionName = "GitHubMcp";

    /// <summary><c>false</c> turns the GitHub MCP tools off; the Copilots then only have their own tools.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The remote GitHub MCP server (Streamable HTTP).</summary>
    public string Endpoint { get; set; } = "https://api.githubcopilot.com/mcp/";

    /// <summary>The comma-separated toolsets the server exposes (<c>X-MCP-Toolsets</c>).</summary>
    public string Toolsets { get; set; } = "repos,issues,pull_requests,actions";
}
