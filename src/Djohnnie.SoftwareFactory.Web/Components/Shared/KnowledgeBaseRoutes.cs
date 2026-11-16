using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>The knowledge base page's URLs, the icon per kind of file and the Markdown link to a file.</summary>
public static class KnowledgeBaseRoutes
{
    public const string Path = "/knowledge-base";

    /// <summary>
    /// The warning on the page and in the upload and edit dialog. Blobs are publicly readable, and their metadata
    /// (title, description and folder path) comes along as response headers.
    /// </summary>
    public const string PublicNotice =
        "Files in the knowledge base are public: anyone with a link can open them, together with their title, description and folder. " +
        "Don't upload confidential data, such as secrets, credentials, personal data or internal documents that may not be shared.";

    /// <summary>The page showing a folder, or the top of the library for <c>null</c>.</summary>
    public static string FolderPath(Guid? folderId)
    {
        return folderId is { } id ? $"{Path}?folder={id}" : Path;
    }

    public static string Icon(KnowledgeFile file)
    {
        var type = file.ContentType.ToLowerInvariant();
        var extension = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        if (type.StartsWith("image/", StringComparison.Ordinal))
        {
            return Icons.Material.Filled.Image;
        }
        if (type.StartsWith("video/", StringComparison.Ordinal))
        {
            return Icons.Material.Filled.Movie;
        }
        if (type.StartsWith("audio/", StringComparison.Ordinal))
        {
            return Icons.Material.Filled.AudioFile;
        }
        if (type == "application/pdf")
        {
            return Icons.Material.Filled.PictureAsPdf;
        }

        return extension switch
        {
            ".zip" or ".7z" or ".gz" or ".tar" or ".rar" => Icons.Material.Filled.FolderZip,
            ".xlsx" or ".xls" or ".csv" or ".ods" => Icons.Material.Filled.TableChart,
            ".pptx" or ".ppt" or ".odp" => Icons.Material.Filled.Slideshow,
            ".json" or ".xml" or ".yaml" or ".yml" or ".cs" or ".js" or ".ts" or ".py" or ".sql" or ".html" => Icons.Material.Filled.Code,
            _ => type.StartsWith("text/", StringComparison.Ordinal) || extension is ".docx" or ".doc" or ".odt" or ".md" or ".rtf"
                ? Icons.Material.Filled.Description
                : Icons.Material.Filled.InsertDriveFile
        };
    }
}
