namespace Djohnnie.SoftwareFactory.Contracts.Common;

public sealed record EmptyRequest;
public sealed record EntityIdRequest(int Id);
public sealed record RepositoryRequest(string RepositoryFullName);
public sealed record RepositoryItemRequest(string RepositoryFullName, int Id);
public sealed record OperationResult;
