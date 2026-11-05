namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// Turns on dictation in a <see cref="MarkdownEditor"/>: what is said is fitted into the description by the Description Writer.
/// </summary>
/// <param name="Subject">What the description is for, e.g. <c>user story</c> or <c>user story template skeleton</c>.</param>
/// <param name="Title">The title of the item, which helps to understand what was said.</param>
/// <param name="Template">The description skeleton of the item's template, whose structure the result follows.</param>
public sealed record DescriptionDictation(string Subject, string? Title = null, string? Template = null);
