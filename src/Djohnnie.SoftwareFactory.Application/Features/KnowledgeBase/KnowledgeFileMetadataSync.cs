using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>
/// Rewrites the blob metadata of files after their details changed in the database (an edited file, or every file
/// below a renamed or moved folder). The database is the source of truth, so a failure is logged and doesn't undo the
/// change: the next edit writes the metadata again.
/// </summary>
public sealed class KnowledgeFileMetadataSync(IKnowledgeFileStorage storage, ILogger<KnowledgeFileMetadataSync> logger)
{
    /// <summary>At most this many blobs are updated at the same time, e.g. after renaming a folder with many files.</summary>
    public const int MaxParallelUpdates = 4;

    /// <returns>The number of files whose metadata could not be updated.</returns>
    public async Task<int> UpdateAsync(IReadOnlyCollection<KnowledgeFile> files, KnowledgeLibrary library, CancellationToken cancellationToken)
    {
        if (!storage.IsConfigured || files.Count == 0)
        {
            return 0;
        }

        var failed = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelUpdates, CancellationToken = cancellationToken },
            async (file, token) =>
            {
                try
                {
                    await storage.SetMetadataAsync(file.Id, KnowledgeFileMetadata.For(file, library), token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref failed);
                    logger.LogWarning(ex, "Could not update the blob metadata of knowledge base file {FileId}.", file.Id);
                }
            });
        return failed;
    }
}
