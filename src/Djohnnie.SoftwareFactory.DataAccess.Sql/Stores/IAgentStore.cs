namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>Local data about the custom agents of a repository. The agent files themselves live on GitHub.</summary>
public interface IAgentStore
{
    /// <summary>
    /// Makes the local agent rows of a repository match the agent files found on GitHub: adds rows for new
    /// agents and removes rows of agents that no longer exist. Returns the file names of the favorite agents.
    /// </summary>
    Task<HashSet<string>> SyncAsync(string repositoryFullName, IEnumerable<string> fileNames, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(string repositoryFullName, string fileName, bool isFavorite, CancellationToken cancellationToken = default);
}
