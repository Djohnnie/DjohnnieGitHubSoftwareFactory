namespace Djohnnie.SoftwareFactory.Contracts.InternalAgents;

/// <summary>The tokens one request to a model used.</summary>
/// <param name="InputTokens">All input tokens, including the cached ones.</param>
/// <param name="OutputTokens">All output tokens, including the reasoning ones.</param>
/// <param name="CachedInputTokens">The part of <paramref name="InputTokens"/> that was read from the cache.</param>
/// <param name="ReasoningTokens">The part of <paramref name="OutputTokens"/> the model used for reasoning; for information, it has no price of its own.</param>
public sealed record ModelUsage(long InputTokens, long OutputTokens, long CachedInputTokens = 0, long ReasoningTokens = 0);
