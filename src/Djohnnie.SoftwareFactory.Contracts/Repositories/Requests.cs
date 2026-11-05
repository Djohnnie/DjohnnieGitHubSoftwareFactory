namespace Djohnnie.SoftwareFactory.Contracts.Repositories;

public sealed record SetRepositoryFavoriteRequest(string RepositoryFullName, bool IsFavorite);
