using Microsoft.Extensions.Logging;
using AgentInfrastructure = Djohnnie.SoftwareFactory.Agents;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// Gives a Copilot read-only GitHub MCP tools for the selected repository, as the signed-in user. When the server is
/// turned off or can't be reached, the Copilot keeps working with its own tools.
/// </summary>
public sealed class GitHubMcpService(
    AgentInfrastructure.GitHubMcpConnector connector,
    IGitHubTokenProvider tokenProvider,
    ILogger<GitHubMcpService> logger)
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The tools for the repository, or <c>null</c> when the GitHub MCP server is turned off or unavailable.</summary>
    public async Task<RepositoryGitHubTools?> ConnectAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        if (!connector.IsEnabled)
        {
            return null;
        }

        // The page waits for the connection, so a slow server must not hold it up for long.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            var token = await tokenProvider.GetAccessTokenAsync();
            var session = await connector.ConnectAsync(token, repositoryFullName, timeout.Token);
            logger.LogInformation("Connected to the GitHub MCP server for {Repository} with {Count} tools.", repositoryFullName, session.Tools.Count);
            return new RepositoryGitHubTools(session);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not connect to the GitHub MCP server for {Repository}; the Copilot only gets its own tools.", repositoryFullName);
            return null;
        }
    }
}

/// <summary>The GitHub MCP tools of one repository; dispose it to close the connection.</summary>
public sealed class RepositoryGitHubTools : IAsyncDisposable
{
    private readonly AgentInfrastructure.GitHubMcpSession _session;

    internal RepositoryGitHubTools(AgentInfrastructure.GitHubMcpSession session)
    {
        _session = session;
    }

    internal AgentInfrastructure.GitHubMcpSession Session
    {
        get
        {
            return _session;
        }
    }

    public int Count
    {
        get
        {
            return _session.Tools.Count;
        }
    }

    public ValueTask DisposeAsync()
    {
        return _session.DisposeAsync();
    }
}
