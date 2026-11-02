namespace Djohnnie.SoftwareFactory.Contracts.Repositories;

public sealed record RecentRepository(string FullName, DateTimeOffset LastAccessedAt);
