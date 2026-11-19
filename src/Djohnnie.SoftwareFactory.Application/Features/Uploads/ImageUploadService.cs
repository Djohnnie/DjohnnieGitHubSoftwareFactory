using Azure.Storage.Blobs;
using Djohnnie.SoftwareFactory.Contracts.Uploads;
using MediatR;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

public sealed class ImageUploadService(ISender mediator, IOptions<ImageUploadOptions> options)
{
    private IReadOnlyList<string>? _prefixes;

    /// <summary>Whether an image storage is configured; otherwise the editors refuse images.</summary>
    public bool IsEnabled => options.Value.IsConfigured;

    public long MaxImageSize => options.Value.MaxImageSize;

    public async Task<UploadedImage> UploadImageAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new UploadImageCommand(new(fileName, content)), cancellationToken)).Dto;
    }

    /// <summary>
    /// Whether the URL is an image in DGHSF's own image storage (an upload or a screenshot of the agent worker), which is safe
    /// to show inline: loading it tells nobody else that the page was opened.
    /// </summary>
    public bool IsStoredImage(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }
        var absolute = uri.AbsoluteUri;
        return Prefixes().Any(prefix => absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Where the stored images are: the public base URL and the blob container itself, each ending with a slash.</summary>
    private IReadOnlyList<string> Prefixes()
    {
        if (_prefixes is not null)
        {
            return _prefixes;
        }

        var value = options.Value;
        var prefixes = new List<string>();
        if (value.IsConfigured)
        {
            if (Uri.TryCreate(value.PublicBaseUrl, UriKind.Absolute, out var baseUrl))
            {
                prefixes.Add($"{baseUrl.AbsoluteUri.TrimEnd('/')}/");
            }
            try
            {
                prefixes.Add($"{new BlobContainerClient(value.ConnectionString, value.ContainerName).Uri.AbsoluteUri.TrimEnd('/')}/");
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // A connection string the storage can't use either; uploads fail with the reason.
            }
        }
        return _prefixes = prefixes;
    }
}
