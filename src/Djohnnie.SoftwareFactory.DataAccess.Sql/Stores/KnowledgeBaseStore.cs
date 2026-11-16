using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>The knowledge base's virtual folders and file metadata, shared across repositories.</summary>
public sealed class KnowledgeBaseStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IKnowledgeBaseStore
{
    public async Task<KnowledgeLibrary> GetLibraryAsync(string? repositoryFullName, CancellationToken cancellationToken = default)
    {
        var normalized = repositoryFullName is null ? null : Entities.Repository.Normalize(repositoryFullName);
        return await LoadLibraryAsync(
            folders => folders.Where(f => normalized != null && f.Repository.FullName == normalized),
            files => files.Where(f => f.RepositoryId == null || f.Repository!.FullName == normalized),
            cancellationToken);
    }

    public async Task<KnowledgeLibrary> GetFullLibraryAsync(CancellationToken cancellationToken = default)
    {
        return await LoadLibraryAsync(folders => folders, files => files, cancellationToken);
    }

    public async Task<KnowledgeFile?> GetFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var file = await db.KnowledgeFiles.AsNoTracking().Include(f => f.Repository).SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        return file is null ? null : ToContract(file);
    }

    private async Task<KnowledgeLibrary> LoadLibraryAsync(
        Func<IQueryable<Entities.KnowledgeFolder>, IQueryable<Entities.KnowledgeFolder>> folderFilter,
        Func<IQueryable<Entities.KnowledgeFile>, IQueryable<Entities.KnowledgeFile>> fileFilter,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var folders = await folderFilter(db.KnowledgeFolders.AsNoTracking().Include(f => f.Repository)).ToListAsync(cancellationToken);
        var files = await fileFilter(db.KnowledgeFiles.AsNoTracking().Include(f => f.Repository)).ToListAsync(cancellationToken);
        return new KnowledgeLibrary(
            folders.Select(ToContract).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Id).ToList(),
            files.Select(ToContract).OrderBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Id).ToList());
    }

    public async Task<KnowledgeFolder> SaveFolderAsync(KnowledgeFolder value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var name = value.Name.Trim();
        Entities.KnowledgeFolder folder;
        if (value.Id == Guid.Empty)
        {
            // Saves the repository row when it's new, so it goes before any other change.
            var repository = await db.GetOrAddRepositoryAsync(value.RepositoryFullName, cancellationToken);
            folder = new Entities.KnowledgeFolder { Repository = repository, RepositoryId = repository.Id, CreatedAt = now };
            db.KnowledgeFolders.Add(folder);
        }
        else
        {
            // A folder stays in its repository; one of another repository is as good as missing.
            var normalized = Entities.Repository.Normalize(value.RepositoryFullName);
            folder = await db.KnowledgeFolders.Include(f => f.Repository)
                .SingleOrDefaultAsync(f => f.Id == value.Id && f.Repository.FullName == normalized, cancellationToken)
                ?? throw new DomainRuleException("The folder no longer exists.");
        }

        if (value.ParentId is { } parentId)
        {
            await EnsureNotInsideItselfAsync(db, folder.RepositoryId, folder.Id, parentId, cancellationToken);
        }

        // The unique index also enforces this; checking first gives a clear message instead of a database error.
        var duplicate = await db.KnowledgeFolders.AnyAsync(
            f => f.Id != folder.Id && f.RepositoryId == folder.RepositoryId && f.ParentId == value.ParentId && f.Name == name, cancellationToken);
        if (duplicate)
        {
            throw new DomainRuleException($"There already is a folder named '{name}' there.");
        }

        folder.ParentId = value.ParentId;
        folder.Name = name;
        folder.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return ToContract(folder);
    }

    public async Task DeleteFolderAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var folder = await db.KnowledgeFolders.SingleOrDefaultAsync(f => f.Id == id && f.Repository.FullName == normalized, cancellationToken);
        if (folder is null)
        {
            return;
        }

        if (await db.KnowledgeFolders.AnyAsync(f => f.ParentId == id, cancellationToken) || await db.KnowledgeFiles.AnyAsync(f => f.FolderId == id, cancellationToken))
        {
            throw new DomainRuleException($"The folder '{folder.Name}' isn't empty. Move or delete its folders and files first.");
        }

        db.KnowledgeFolders.Remove(folder);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<KnowledgeFile> AddFileAsync(KnowledgeFile value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Saves the repository row when it's new, so it goes before any other change.
        var repository = await GetRepositoryAsync(db, value.RepositoryFullName, cancellationToken);
        await EnsureFolderFitsAsync(db, value.FolderId, repository, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var file = new Entities.KnowledgeFile
        {
            Id = value.Id,
            FolderId = value.FolderId,
            Repository = repository,
            Title = value.Title.Trim(),
            Description = value.Description.Trim(),
            FileName = value.FileName,
            ContentType = value.ContentType,
            Size = value.Size,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.KnowledgeFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        return ToContract(file);
    }

    public async Task<KnowledgeFile> UpdateFileAsync(KnowledgeFile value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var repository = await GetRepositoryAsync(db, value.RepositoryFullName, cancellationToken);
        var file = await db.KnowledgeFiles.Include(f => f.Repository).SingleOrDefaultAsync(f => f.Id == value.Id, cancellationToken)
            ?? throw new DomainRuleException("The file no longer exists.");
        await EnsureFolderFitsAsync(db, value.FolderId, repository, cancellationToken);
        file.FolderId = value.FolderId;
        file.Repository = repository;
        file.RepositoryId = repository?.Id;
        file.Title = value.Title.Trim();
        file.Description = value.Description.Trim();
        file.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return ToContract(file);
    }

    public async Task<bool> DeleteFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var file = await db.KnowledgeFiles.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file is null)
        {
            return false;
        }

        db.KnowledgeFiles.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>The repository row (added when missing), or <c>null</c> for a global file.</summary>
    private static async Task<Entities.Repository?> GetRepositoryAsync(AppDbContext db, string? repositoryFullName, CancellationToken cancellationToken)
    {
        return repositoryFullName is null ? null : await db.GetOrAddRepositoryAsync(repositoryFullName, cancellationToken);
    }

    /// <summary>A file can only be in a folder of its own repository; a global file is never in a folder.</summary>
    private static async Task EnsureFolderFitsAsync(AppDbContext db, Guid? folderId, Entities.Repository? repository, CancellationToken cancellationToken)
    {
        if (folderId is not { } id)
        {
            return;
        }
        if (repository is null)
        {
            throw new DomainRuleException("A global file can't be in a folder; folders belong to a repository.");
        }
        if (!await db.KnowledgeFolders.AnyAsync(f => f.Id == id && f.RepositoryId == repository.Id, cancellationToken))
        {
            throw new DomainRuleException("The folder no longer exists in this repository.");
        }
    }

    /// <summary>Walks up from the new parent (of the same repository); meeting the folder itself means the move would make a cycle.</summary>
    private static async Task EnsureNotInsideItselfAsync(AppDbContext db, Guid repositoryId, Guid folderId, Guid parentId, CancellationToken cancellationToken)
    {
        var parents = await db.KnowledgeFolders.AsNoTracking()
            .Where(f => f.RepositoryId == repositoryId)
            .Select(f => new { f.Id, f.ParentId })
            .ToDictionaryAsync(f => f.Id, f => f.ParentId, cancellationToken);
        if (!parents.ContainsKey(parentId))
        {
            throw new DomainRuleException("The parent folder no longer exists.");
        }

        var seen = new HashSet<Guid>();
        for (Guid? current = parentId; current is { } id && seen.Add(id); current = parents.GetValueOrDefault(id))
        {
            if (id == folderId)
            {
                throw new DomainRuleException("A folder can't be moved into itself or one of its own folders.");
            }
        }
    }

    private static KnowledgeFolder ToContract(Entities.KnowledgeFolder folder)
    {
        return new KnowledgeFolder
        {
            Id = folder.Id,
            RepositoryFullName = folder.Repository.FullName,
            ParentId = folder.ParentId,
            Name = folder.Name,
            CreatedAt = folder.CreatedAt,
            UpdatedAt = folder.UpdatedAt
        };
    }

    private static KnowledgeFile ToContract(Entities.KnowledgeFile file)
    {
        return new KnowledgeFile
        {
            Id = file.Id,
            FolderId = file.FolderId,
            RepositoryFullName = file.Repository?.FullName,
            Title = file.Title,
            Description = file.Description,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Size,
            CreatedAt = file.CreatedAt,
            UpdatedAt = file.UpdatedAt
        };
    }
}
