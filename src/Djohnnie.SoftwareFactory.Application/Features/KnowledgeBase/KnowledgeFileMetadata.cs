using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>
/// The file's details that are copied to its blob's metadata, so the storage account can be understood without the
/// database and Azure AI Search can filter the file's passages per repository. The database stays the source of truth;
/// the metadata is rewritten whenever these change.
/// </summary>
/// <param name="Path">The virtual folder path, like <c>/Specs/API</c>; <c>/</c> for the top of the library.</param>
/// <param name="Repository">The repository the file belongs to (lowercase <c>owner/name</c>), or <c>null</c> for a global file.</param>
public sealed record KnowledgeFileMetadata(string Title, string Description, string Path, string? Repository = null)
{
    public static KnowledgeFileMetadata For(KnowledgeFile file, KnowledgeLibrary library)
    {
        return new(file.Title.Trim(), file.Description.Trim(), library.VirtualPath(file.FolderId), file.RepositoryFullName?.Trim().ToLowerInvariant());
    }
}
