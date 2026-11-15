using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>Keeps snapshots as blobs named <c>{runId}/{name}</c> in the configured private container.</summary>
public sealed class AzureBlobRepositorySnapshotStorage(IOptions<RepositorySnapshotOptions> options) : IRepositorySnapshotStorage
{
    private readonly Lazy<BlobContainerClient> _container = new(() =>
    {
        var value = options.Value;
        if (!value.IsConfigured)
        {
            throw new InvalidOperationException($"{RepositorySnapshotOptions.SectionName} is not configured.");
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

    public async Task WriteAsync(Guid runId, string name, byte[] content, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        await _container.Value.GetBlobClient(BlobName(runId, name)).UploadAsync(new BinaryData(content), overwrite: true, cancellationToken);
    }

    public async Task<byte[]?> ReadAsync(Guid runId, string name, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _container.Value.GetBlobClient(BlobName(runId, name)).DownloadContentAsync(cancellationToken);
            return result.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Guid>> GetRunsAsync(CancellationToken cancellationToken)
    {
        var runs = new List<Guid>();
        try
        {
            await foreach (var item in _container.Value.GetBlobsByHierarchyAsync(BlobTraits.None, BlobStates.None, "/", null, cancellationToken))
            {
                if (item.IsPrefix && Guid.TryParse(item.Prefix.TrimEnd('/'), out var runId))
                {
                    runs.Add(runId);
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The container doesn't exist yet: no run made a snapshot.
        }
        return runs;
    }

    public async Task DeleteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await foreach (var blob in _container.Value.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{runId}/", cancellationToken))
        {
            names.Add(blob.Name);
        }
        await Parallel.ForEachAsync(names, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken }, async (name, token) =>
        {
            await _container.Value.DeleteBlobIfExistsAsync(name, cancellationToken: token);
        });
    }

    /// <summary>Creates the container (without anonymous access) the first time a snapshot is written.</summary>
    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _containerChecked) == 0)
        {
            await _container.Value.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            Volatile.Write(ref _containerChecked, 1);
        }
    }

    private static string BlobName(Guid runId, string name)
    {
        return $"{runId}/{name}";
    }
}
