namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>Stores the knowledge base files as publicly readable blobs named after the file's id.</summary>
public interface IKnowledgeFileStorage
{
    bool IsConfigured { get; }

    /// <summary>Stores a new blob with its metadata; it is served with <paramref name="fileName"/> and <paramref name="contentType"/>.</summary>
    Task StoreAsync(Guid id, Stream content, string fileName, string contentType, KnowledgeFileMetadata metadata, CancellationToken cancellationToken);

    /// <summary>Replaces the metadata of an existing blob; the content and its headers stay as they are.</summary>
    Task SetMetadataAsync(Guid id, KnowledgeFileMetadata metadata, CancellationToken cancellationToken);

    /// <summary>Deletes the blob, if it exists.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The public link to the blob; empty when the storage is not configured.</summary>
    string GetUrl(Guid id);
}
