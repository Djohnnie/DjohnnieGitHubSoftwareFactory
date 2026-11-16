namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>
/// The Azure Storage blob container that holds the knowledge base files, one blob per file named after its id. The
/// container must allow anonymous blob read access, so links in work items (and GitHub issues) open for everyone with
/// the link. Unconfigured means the knowledge base can't store files.
/// </summary>
public sealed class KnowledgeBaseOptions
{
    public const string SectionName = "KnowledgeBase";

    /// <summary>The storage account connection string.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>A separate public container; don't share it with the image uploads or the private repository snapshots.</summary>
    public string ContainerName { get; set; } = "dghsf-knowledge-base";

    /// <summary>Optional base URL (e.g. a CDN or custom domain) that replaces the blob container URL in links.</summary>
    public string? PublicBaseUrl { get; set; }

    public int MaxFileSizeInMegabytes { get; set; } = 100;

    public long MaxFileSize => MaxFileSizeInMegabytes * 1024L * 1024L;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(ContainerName);
}
