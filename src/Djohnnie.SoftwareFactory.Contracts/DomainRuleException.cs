namespace Djohnnie.SoftwareFactory.Contracts;

/// <summary>
/// A domain rule refused the operation, e.g. accepting an item that is no longer a draft or deleting a parent that
/// still has children. The message is written for the user. Separate from <see cref="InvalidOperationException"/>, so
/// callers can show these messages while real bugs still surface as errors.
/// </summary>
public sealed class DomainRuleException(string message) : Exception(message);
