namespace Djohnnie.SoftwareFactory.Contracts;

public sealed class PersistenceException(string message, Exception innerException) : Exception(message, innerException);
