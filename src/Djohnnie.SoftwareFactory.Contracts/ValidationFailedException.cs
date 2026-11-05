namespace Djohnnie.SoftwareFactory.Contracts;

/// <summary>
/// A request was refused by its validators before it was handled. Separate from <see cref="InvalidOperationException"/>,
/// so callers can tell invalid input apart from a refused operation or a bug.
/// </summary>
public sealed class ValidationFailedException(IReadOnlyList<string> errors)
    : Exception(string.Join(" ", errors))
{
    /// <summary>The distinct validation messages, in the order the validators reported them.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;
}
