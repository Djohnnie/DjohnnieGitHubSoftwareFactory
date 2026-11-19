namespace Djohnnie.SoftwareFactory.Contracts.Repositories;

public sealed record SetRepositoryFavoriteRequest(string RepositoryFullName, bool IsFavorite);

public sealed record SetRepositoryArchivedRequest(string RepositoryFullName, bool IsArchived);

/// <param name="Changed">Whether the repository's state changed; <c>false</c> when it already was in that state or is unknown.</param>
public sealed record SetRepositoryArchivedResult(bool Changed);
