namespace Djohnnie.SoftwareFactory.Contracts.Common;

public sealed record CollectionResult<T>(IReadOnlyList<T> Items);
