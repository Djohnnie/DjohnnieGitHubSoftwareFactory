using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

/// <summary>Stores images in the configured (publicly readable) Azure Storage blob container.</summary>
public sealed class AzureBlobImageStorage(IOptions<ImageUploadOptions> options) : IImageStorage
{
    private readonly Lazy<BlobContainerClient> _container = new(() =>
    {
        var value = options.Value;
        if (!value.IsConfigured)
        {
            throw new InvalidOperationException($"{ImageUploadOptions.SectionName} is not configured.");
        }

        return new BlobContainerClient(value.ConnectionString, value.ContainerName);
    });

    public async Task<string> StoreAsync(string name, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var blob = _container.Value.GetBlobClient(name);
        await blob.UploadAsync(new BinaryData(content), new BlobUploadOptions
        {
            // Names are unique, so the content never changes.
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = "public, max-age=31536000, immutable" },
            Conditions = new BlobRequestConditions { IfNoneMatch = Azure.ETag.All }
        }, cancellationToken);

        var baseUrl = options.Value.PublicBaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl) ? blob.Uri.AbsoluteUri : $"{baseUrl.TrimEnd('/')}/{name}";
    }
}
