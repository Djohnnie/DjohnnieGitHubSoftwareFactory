namespace Djohnnie.SoftwareFactory.Contracts.Speech;

/// <summary>A dictated text for the Description Writer to merge into a (Markdown) description.</summary>
/// <param name="Transcript">What the user said.</param>
/// <param name="Subject">What the description is for, e.g. <c>user story</c> or <c>user story template skeleton</c>.</param>
/// <param name="Title">The title of the item, if any, to understand what was said.</param>
/// <param name="Description">The current description, kept and merged with what was said.</param>
/// <param name="Template">The description skeleton of the item's template, if any, whose structure the result follows.</param>
public sealed record WriteDictatedDescriptionRequest(string Transcript, string Subject, string? Title, string? Description, string? Template)
{
    public const int MaxTranscriptLength = 20_000;
}

/// <param name="Description">The new description.</param>
/// <param name="Written">
/// <c>true</c> when the Description Writer merged the text; <c>false</c> when the transcript was added as-is at the end
/// because the agent is disabled, not configured or failed.
/// </param>
public sealed record DictatedDescriptionResult(string Description, bool Written);
