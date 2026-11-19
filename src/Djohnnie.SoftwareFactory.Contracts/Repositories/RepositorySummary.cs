namespace Djohnnie.SoftwareFactory.Contracts.Repositories;

/// <param name="FullName">The repository as <c>owner/name</c>, in lower case.</param>
/// <param name="LastAccessedAt">When the repository was last opened, or <c>null</c> if it never was.</param>
/// <param name="IsArchived">The sync found the repository removed from GitHub; what DGHSF stored can still be opened.</param>
/// <param name="ArchivedAt">When the sync found it removed, or <c>null</c> unless <paramref name="IsArchived"/>.</param>
public sealed record RepositorySummary(Guid Id, string FullName, string Name, DateTimeOffset? LastAccessedAt, bool IsFavorite,
    bool IsArchived = false, DateTimeOffset? ArchivedAt = null);
