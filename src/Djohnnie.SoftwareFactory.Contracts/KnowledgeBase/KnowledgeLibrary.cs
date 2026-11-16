namespace Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;

/// <summary>All folders and files of the knowledge base, which is shared across repositories.</summary>
public sealed record KnowledgeLibrary(IReadOnlyList<KnowledgeFolder> Folders, IReadOnlyList<KnowledgeFile> Files)
{
    public static readonly KnowledgeLibrary Empty = new([], []);

    public KnowledgeFolder? FindFolder(Guid? id)
    {
        return id is null ? null : Folders.FirstOrDefault(folder => folder.Id == id);
    }

    /// <summary>The folders from the top of the library down to <paramref name="id"/> itself; empty for the top.</summary>
    public List<KnowledgeFolder> PathOf(Guid? id)
    {
        var path = new List<KnowledgeFolder>();
        var seen = new HashSet<Guid>();
        for (var folder = FindFolder(id); folder is not null && seen.Add(folder.Id); folder = FindFolder(folder.ParentId))
        {
            path.Insert(0, folder);
        }

        return path;
    }

    /// <summary>The folder's path, like "Specs / API"; empty for the top of the library.</summary>
    public string PathName(Guid? id)
    {
        return string.Join(" / ", PathOf(id).Select(folder => folder.Name));
    }

    /// <summary>The folder's path with slashes, like "/Specs/API"; "/" for the top of the library (folder names can't contain slashes).</summary>
    public string VirtualPath(Guid? id)
    {
        return "/" + string.Join("/", PathOf(id).Select(folder => folder.Name));
    }

    /// <summary>The ids of the folder and every folder below it.</summary>
    public HashSet<Guid> SubtreeOf(Guid id)
    {
        var result = new HashSet<Guid> { id };
        var queue = new Queue<Guid>([id]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var child in Folders.Where(folder => folder.ParentId == current))
            {
                if (result.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return result;
    }
}
