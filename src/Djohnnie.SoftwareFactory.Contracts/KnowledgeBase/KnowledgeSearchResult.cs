namespace Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;

/// <summary>A passage of a knowledge base file that matches a search, with the file it comes from.</summary>
/// <param name="FileId">The file the passage is from.</param>
/// <param name="Title">The file's current title.</param>
/// <param name="Url">The public link to the file, to cite or put in a work item.</param>
/// <param name="Folder">The file's virtual folder, like <c>/Specs/API</c>.</param>
/// <param name="Repository">The repository the file belongs to, or <c>null</c> for a global file.</param>
/// <param name="Text">The matching passage (a chunk of the file's text).</param>
public sealed record KnowledgeSearchResult(Guid FileId, string Title, string Url, string Folder, string? Repository, string Text);
