namespace Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;

/// <summary>The folders, the global files and, with a repository, that repository's files.</summary>
/// <param name="RepositoryFullName">The selected repository, or <c>null</c> for the global files only.</param>
public sealed record KnowledgeLibraryRequest(string? RepositoryFullName);

/// <summary>Creates (empty id) or renames and moves a folder.</summary>
public sealed record SaveKnowledgeFolderRequest(KnowledgeFolder Folder);

/// <summary>
/// Changes the title, description, folder and repository of a file; the content can't be replaced, so its link stays valid.
/// </summary>
public sealed record SaveKnowledgeFileRequest(KnowledgeFile File);

/// <param name="FolderId">The virtual folder, or <c>null</c> for the top of the library.</param>
/// <param name="FileName">The original file name, served with the blob.</param>
/// <param name="ContentType">The content type the browser reported; empty for unknown.</param>
/// <param name="Size">The size in bytes, checked against the maximum before the content is read.</param>
/// <param name="Content">The file content, read once while it is uploaded.</param>
/// <param name="RepositoryFullName">The repository the file belongs to, or <c>null</c> for a global file.</param>
public sealed record UploadKnowledgeFileRequest(Guid? FolderId, string Title, string Description, string FileName, string ContentType, long Size, Stream Content,
    string? RepositoryFullName = null);

/// <summary>Searches the content of the global files and the repository's files.</summary>
public sealed record SearchKnowledgeBaseRequest(string RepositoryFullName, string Query);
