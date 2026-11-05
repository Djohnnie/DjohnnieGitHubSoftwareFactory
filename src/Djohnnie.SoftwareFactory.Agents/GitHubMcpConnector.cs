using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Connects to the remote GitHub MCP server as the signed-in user and returns its read-only tools, each pinned to the
/// selected repository with <see cref="RepositoryScopedFunction"/>.
/// </summary>
public sealed class GitHubMcpConnector(IOptions<GitHubMcpOptions> options, ILoggerFactory loggerFactory)
{
    /// <summary>Tools that are left out: the Copilots have their own file tools, and these don't belong to one repository.</summary>
    public static readonly IReadOnlySet<string> ExcludedTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "get_file_contents", "search_repositories", "search_users", "search_orgs"
    };

    public bool IsEnabled
    {
        get
        {
            return options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.Endpoint);
        }
    }

    /// <summary>Connects and lists the tools; the caller disposes the session when the conversation ends.</summary>
    public async Task<GitHubMcpSession> ConnectAsync(string accessToken, string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "GitHub",
            Endpoint = new Uri(settings.Endpoint.Trim()),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {accessToken}",
                ["User-Agent"] = "DGHSF/1.0",
                ["X-MCP-Readonly"] = "true",
                ["X-MCP-Toolsets"] = settings.Toolsets
            }
        }, loggerFactory);

        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return new GitHubMcpSession(client, transport, Select(tools, repositoryFullName));
        }
        catch
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }
            await transport.DisposeAsync();
            throw;
        }
    }

    /// <summary>The allowed tools, pinned to the repository.</summary>
    public static IReadOnlyList<AITool> Select(IEnumerable<McpClientTool> tools, string repositoryFullName)
    {
        return tools
            .Where(t => IsAllowed(t.ProtocolTool))
            .Select(AITool (t) => new RepositoryScopedFunction(t, repositoryFullName))
            .ToList();
    }

    /// <summary>
    /// A tool is allowed when the server marks it read-only and it can be pinned to one repository: it takes an
    /// <c>owner</c> and <c>repo</c>, or it is a search tool whose <c>query</c> gets a <c>repo:</c> qualifier.
    /// </summary>
    public static bool IsAllowed(Tool tool)
    {
        if (tool.Annotations?.ReadOnlyHint != true || ExcludedTools.Contains(tool.Name))
        {
            return false;
        }

        return RepositoryScopedFunction.TakesRepository(tool.InputSchema) || IsSearchTool(tool);
    }

    private static bool IsSearchTool(Tool tool)
    {
        return tool.Name.StartsWith("search_", StringComparison.Ordinal)
            && tool.InputSchema.TryGetProperty("properties", out var properties)
            && properties.TryGetProperty(RepositoryScopedFunction.QueryParameter, out _);
    }
}

/// <summary>An open connection to the GitHub MCP server with the tools for one repository.</summary>
public sealed class GitHubMcpSession : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly IAsyncDisposable _transport;

    internal GitHubMcpSession(McpClient client, IAsyncDisposable transport, IReadOnlyList<AITool> tools)
    {
        _client = client;
        _transport = transport;
        Tools = tools;
    }

    public IReadOnlyList<AITool> Tools { get; }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _transport.DisposeAsync();
    }
}
