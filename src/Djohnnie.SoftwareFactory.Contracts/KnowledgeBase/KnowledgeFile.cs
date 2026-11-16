namespace Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;

/// <summary>
/// A file of the knowledge base. The content is a blob named after <see cref="Id"/> in a public blob container, so
/// <see cref="Url"/> can be linked from work items; the title, description and folder are stored in the database.
/// </summary>
public sealed record KnowledgeFile
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2_000;
    public const int MaxFileNameLength = 255;
    public const int MaxContentTypeLength = 200;

    public Guid Id { get; set; }

    /// <summary>The virtual folder (of the file's repository), or <c>null</c> for a file at the top of the library; always <c>null</c> for a global file.</summary>
    public Guid? FolderId { get; set; }

    /// <summary>The repository the file belongs to (lowercase <c>owner/name</c>), or <c>null</c> for a global file that every repository sees.</summary>
    public string? RepositoryFullName { get; set; }

    public bool IsGlobal => RepositoryFullName is null;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The name of the uploaded file; the blob is served with it, so a download gets this name.</summary>
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    /// <summary>The size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>The public link to the blob; empty when the file storage is not configured.</summary>
    public string Url { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public KnowledgeFile Copy()
    {
        return this with { };
    }

    /// <summary>A size like "12 KB" or "3.4 MB".</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size.ToString(size < 10 ? "0.#" : "0", System.Globalization.CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
