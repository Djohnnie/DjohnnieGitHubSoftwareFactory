namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

/// <summary>
/// The Azure Storage blob container that images pasted into the Markdown editors are uploaded to. The container must
/// allow anonymous blob read access so GitHub (and the Copilot cloud agent) can load the images from the issue body.
/// Unconfigured means image upload is disabled.
/// </summary>
public sealed class ImageUploadOptions
{
    public const string SectionName = "ImageUploads";

    /// <summary>The storage account connection string.</summary>
    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "dghsf-uploads";

    /// <summary>Optional base URL (e.g. a CDN or custom domain) that replaces the blob container URL in links.</summary>
    public string? PublicBaseUrl { get; set; }

    public int MaxImageSizeInMegabytes { get; set; } = 10;

    public long MaxImageSize => MaxImageSizeInMegabytes * 1024L * 1024L;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(ContainerName);
}
