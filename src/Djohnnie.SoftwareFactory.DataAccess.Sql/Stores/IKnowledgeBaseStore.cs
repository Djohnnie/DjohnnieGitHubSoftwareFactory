using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>The folders and file metadata of the knowledge base; the file contents are blobs outside the database.</summary>
public interface IKnowledgeBaseStore
{
    /// <summary>
    /// The global files, and the folders and files of <paramref name="repositoryFullName"/> (none with <c>null</c>), by
    /// name and title (the <see cref="KnowledgeFile.Url"/>s are left empty).
    /// </summary>
    Task<KnowledgeLibrary> GetLibraryAsync(string? repositoryFullName, CancellationToken cancellationToken = default);

    /// <summary>All folders and the files of every repository, e.g. to know the paths of the files below a renamed folder.</summary>
    Task<KnowledgeLibrary> GetFullLibraryAsync(CancellationToken cancellationToken = default);

    Task<KnowledgeFile?> GetFileAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates (empty id) a folder in <see cref="KnowledgeFolder.RepositoryFullName"/>, or renames and moves one within
    /// that repository (a folder never changes repository).
    /// </summary>
    /// <exception cref="Contracts.DomainRuleException">
    /// The folder or its new parent no longer exists in the repository, it would end up inside itself, or its parent already has a folder with that name.
    /// </exception>
    Task<KnowledgeFolder> SaveFolderAsync(KnowledgeFolder value, CancellationToken cancellationToken = default);

    /// <exception cref="Contracts.DomainRuleException">The folder still contains folders or files.</exception>
    Task DeleteFolderAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a file with the id its blob was stored under.</summary>
    /// <exception cref="Contracts.DomainRuleException">It's global and in a folder, or its folder no longer exists in its repository.</exception>
    Task<KnowledgeFile> AddFileAsync(KnowledgeFile value, CancellationToken cancellationToken = default);

    /// <summary>Changes the title, description, folder and repository (global with <c>null</c>) of a file; nothing else.</summary>
    /// <exception cref="Contracts.DomainRuleException">The file or its new folder no longer exists.</exception>
    Task<KnowledgeFile> UpdateFileAsync(KnowledgeFile value, CancellationToken cancellationToken = default);

    /// <returns>Whether the file existed.</returns>
    Task<bool> DeleteFileAsync(Guid id, CancellationToken cancellationToken = default);
}
