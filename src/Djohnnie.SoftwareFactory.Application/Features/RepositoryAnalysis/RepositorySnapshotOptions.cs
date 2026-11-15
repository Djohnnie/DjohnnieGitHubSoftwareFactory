namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>
/// The private Azure Storage blob container that a run's copy of the repository is kept in while it runs (section
/// <c>RepositorySnapshots</c>). Unconfigured means every task reads its files from the GitHub API instead.
/// </summary>
public sealed class RepositorySnapshotOptions
{
    public const string SectionName = "RepositorySnapshots";

    /// <summary>The storage account connection string.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>A container without anonymous access: it holds the repository's code.</summary>
    public string ContainerName { get; set; } = "repository-snapshots";

    /// <summary>The most a snapshot may hold after filtering; a larger repository has to be analyzed per folder.</summary>
    public int MaxSizeInMegabytes { get; set; } = 100;

    /// <summary>The most files a snapshot may hold after filtering.</summary>
    public int MaxFiles { get; set; } = 20_000;

    public long MaxSize
    {
        get
        {
            return MaxSizeInMegabytes * 1024L * 1024L;
        }
    }

    public bool IsConfigured
    {
        get
        {
            return !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(ContainerName);
        }
    }
}
