namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>
/// Keeps a run's copy of the repository: its files and a manifest, all under the run's id. A run's tasks read their
/// files here instead of from the GitHub API; the copy is deleted when the run has ended.
/// </summary>
public interface IRepositorySnapshotStorage
{
    bool IsConfigured { get; }

    /// <summary>Stores (or replaces) one file of the run's snapshot.</summary>
    Task WriteAsync(Guid runId, string name, byte[] content, CancellationToken cancellationToken);

    /// <summary>One file of the run's snapshot, or <c>null</c> when it doesn't exist.</summary>
    Task<byte[]?> ReadAsync(Guid runId, string name, CancellationToken cancellationToken);

    /// <summary>The runs that have a snapshot.</summary>
    Task<IReadOnlyList<Guid>> GetRunsAsync(CancellationToken cancellationToken);

    /// <summary>Deletes everything of the run's snapshot.</summary>
    Task DeleteAsync(Guid runId, CancellationToken cancellationToken);
}
