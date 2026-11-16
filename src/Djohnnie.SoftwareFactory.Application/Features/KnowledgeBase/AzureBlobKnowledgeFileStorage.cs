using System.Net.Http.Headers;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>Keeps the knowledge base files as blobs named <c>{id}</c> in the configured public container.</summary>
public sealed class AzureBlobKnowledgeFileStorage(IOptions<KnowledgeBaseOptions> options) : IKnowledgeFileStorage
{
    public const string TitleKey = "title";
    public const string DescriptionKey = "description";
    public const string PathKey = "path";
    public const string ScopeKey = "scope";
    public const string RepositoryKey = "repository";
    public const string GlobalScope = "global";
    public const string RepositoryScope = "repository";

    // Together well below the 8 KB Azure allows for all metadata of a blob (keys included). A title of the maximum
    // length always fits: every character takes at most 9 encoded characters.
    public const int MaxEncodedTitleLength = KnowledgeFile.MaxTitleLength * 9;
    public const int MaxEncodedPathLength = 2_000;
    public const int MaxEncodedDescriptionLength = 4_000;

    private const string Ellipsis = "…";

    /// <summary>Types a browser would run as a page or script; they are always downloaded instead of shown.</summary>
    private static readonly string[] ActiveContentTypes =
        ["text/html", "application/xhtml+xml", "image/svg+xml", "text/xml", "application/xml", "text/javascript", "application/javascript"];

    private readonly Lazy<BlobContainerClient> _container = new(() =>
    {
        var value = options.Value;
        if (!value.IsConfigured)
        {
            throw new InvalidOperationException($"{KnowledgeBaseOptions.SectionName} is not configured.");
        }

        return new BlobContainerClient(value.ConnectionString, value.ContainerName);
    });

    private int _containerChecked;

    public bool IsConfigured
    {
        get
        {
            return options.Value.IsConfigured;
        }
    }

    public async Task StoreAsync(Guid id, Stream content, string fileName, string contentType, KnowledgeFileMetadata metadata, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        await _container.Value.GetBlobClient(BlobName(id)).UploadAsync(content, new BlobUploadOptions
        {
            Metadata = ToBlobMetadata(metadata),
            // The content of a file never changes (it can't be replaced), so its link can be cached for good.
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType,
                ContentDisposition = ContentDisposition(fileName, contentType),
                CacheControl = "public, max-age=31536000, immutable"
            },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, cancellationToken);
    }

    public async Task SetMetadataAsync(Guid id, KnowledgeFileMetadata metadata, CancellationToken cancellationToken)
    {
        await _container.Value.GetBlobClient(BlobName(id)).SetMetadataAsync(ToBlobMetadata(metadata), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The blob metadata: <c>title</c>, <c>description</c> and <c>path</c>, percent-encoded as UTF-8 because metadata values
    /// travel as HTTP headers and must be ASCII. All metadata of a blob must fit in 8 KB, so each value has a budget and a
    /// value that doesn't fit is shortened (ending in "…"); an empty description is left out.
    /// <c>scope</c> (<see cref="GlobalScope"/> or <see cref="RepositoryScope"/>) and <c>repository</c> (only for a
    /// repository's file) are not encoded: they are ASCII already (a repository name is lowercase letters, digits,
    /// <c>-_./</c>), and Azure AI Search copies them as they are to every passage of the file to filter on.
    /// </summary>
    public static Dictionary<string, string> ToBlobMetadata(KnowledgeFileMetadata metadata)
    {
        var result = new Dictionary<string, string>
        {
            [TitleKey] = Encode(metadata.Title, MaxEncodedTitleLength),
            [PathKey] = Encode(metadata.Path, MaxEncodedPathLength),
            [ScopeKey] = metadata.Repository is null ? GlobalScope : RepositoryScope
        };
        if (!string.IsNullOrWhiteSpace(metadata.Description))
        {
            result[DescriptionKey] = Encode(metadata.Description, MaxEncodedDescriptionLength);
        }
        if (metadata.Repository is { } repository)
        {
            result[RepositoryKey] = repository;
        }

        return result;
    }

    /// <summary>The value percent-encoded, cut short (at a character boundary, with an ellipsis) to at most <paramref name="maxLength"/> characters.</summary>
    public static string Encode(string value, int maxLength)
    {
        var encoded = Uri.EscapeDataString(value);
        if (encoded.Length <= maxLength)
        {
            return encoded;
        }

        // Find the longest prefix that still fits together with the ellipsis.
        int low = 0, high = value.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Uri.EscapeDataString(Prefix(value, middle) + Ellipsis).Length <= maxLength)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Uri.EscapeDataString(Prefix(value, low).TrimEnd() + Ellipsis);
    }

    /// <summary>The first <paramref name="length"/> characters, without half a surrogate pair at the end.</summary>
    private static string Prefix(string value, int length)
    {
        if (length > 0 && length < value.Length && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _container.Value.GetBlobClient(BlobName(id)).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    public string GetUrl(Guid id)
    {
        if (!IsConfigured)
        {
            return string.Empty;
        }

        var baseUrl = options.Value.PublicBaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl) ? _container.Value.GetBlobClient(BlobName(id)).Uri.AbsoluteUri : $"{baseUrl.TrimEnd('/')}/{BlobName(id)}";
    }

    /// <summary>The blob is named after the file's id only; the (possibly confidential) title and file name stay out of the link.</summary>
    public static string BlobName(Guid id)
    {
        return id.ToString();
    }

    /// <summary>Shows the file in the browser where that is safe, and always gives a download its original name.</summary>
    public static string ContentDisposition(string fileName, string contentType)
    {
        var active = ActiveContentTypes.Any(type => contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase));
        var header = new ContentDispositionHeaderValue(active ? "attachment" : "inline")
        {
            // A plain ASCII name for old clients, and the real name (RFC 5987) for the rest.
            FileName = $"\"{AsciiFileName(fileName)}\"",
            FileNameStar = fileName
        };
        return header.ToString();
    }

    private static string AsciiFileName(string fileName)
    {
        var builder = new StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            builder.Append(character is >= ' ' and <= '~' and not '"' and not '\\' ? character : '_');
        }

        return builder.ToString();
    }

    /// <summary>Creates the container with anonymous blob read access (not listing) the first time it is needed, e.g. on Azurite.</summary>
    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _containerChecked) == 1)
        {
            return;
        }

        await _container.Value.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        Volatile.Write(ref _containerChecked, 1);
    }
}
