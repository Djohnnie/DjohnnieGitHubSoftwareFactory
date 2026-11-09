namespace Djohnnie.SoftwareFactory.Contracts.Repositories;

/// <param name="FullName">The repository as <c>owner/name</c>, in lower case.</param>
/// <param name="LastAccessedAt">When the repository was last opened, or <c>null</c> if it never was.</param>
public sealed record RepositorySummary(Guid Id, string FullName, string Name, DateTimeOffset? LastAccessedAt, bool IsFavorite);
