namespace Djohnnie.SoftwareFactory.Contracts.Agents;

/// <param name="FileNames">The agent ids (file names without <c>.agent.md</c>) currently in the repository.</param>
public sealed record SyncAgentsRequest(string RepositoryFullName, IReadOnlyList<string> FileNames);

public sealed record SetAgentFavoriteRequest(string RepositoryFullName, string FileName, bool IsFavorite);
