using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>
/// Gives the analysis agents an outline of a repository and tools to read its files. With snapshot storage configured,
/// the first task of a run downloads the whole repository as one archive (one GitHub request) and keeps the readable
/// files under the run's id, so all its tasks read the same commit without using the GitHub API. Without it, every task
/// reads the tree and the files it needs from the GitHub API.
/// </summary>
public sealed class RepositoryFiles(
    GitHubApiClient github,
    IRepositorySnapshotStorage storage,
    IOptions<RepositorySnapshotOptions> options,
    ILogger<RepositoryFiles> logger)
{
    public const int MaxOutlineLength = 40_000;

    private const string ManifestName = "manifest.json";
    private const string FilesFolder = "files/";
    private const int UploadParallelism = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <param name="runId">The run (root task); its tasks share one snapshot.</param>
    /// <exception cref="DomainRuleException">The repository (folder) has no readable files, or is too large for a snapshot.</exception>
    public async Task<RepositorySnapshot> ReadAsync(string repositoryFullName, string? pathPrefix, Guid runId, CancellationToken cancellationToken)
    {
        if (!storage.IsConfigured)
        {
            return await ReadFromApiAsync(repositoryFullName, pathPrefix, cancellationToken);
        }

        // A task of the run made it already; a missing one (a retry, or deleted by a lifecycle rule) is made again.
        var manifest = await ReadManifestAsync(runId, cancellationToken)
            ?? await CreateSnapshotAsync(repositoryFullName, pathPrefix, runId, cancellationToken);
        var files = manifest.Files.Select(file => new GitHubTreeEntry(file.Path, "blob", file.Size)).ToList();
        var paths = files.Select(file => file.Path).ToList();
        return new RepositorySnapshot(paths, RepositoryTreeFilter.Outline(files, MaxOutlineLength), false,
            maxReads => new RepositoryExplorerTools(paths, async (path, token) =>
            {
                var content = await storage.ReadAsync(runId, FilesFolder + path, token);
                return content is null ? null : Encoding.UTF8.GetString(content);
            }, maxReads));
    }

    /// <summary>Downloads the repository, keeps the readable files within the folder and stores them with a manifest (written last).</summary>
    private async Task<SnapshotManifest> CreateSnapshotAsync(string repositoryFullName, string? pathPrefix, Guid runId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var prefix = RepositoryTreeFilter.NormalizePrefix(pathPrefix);
        var files = new List<(GitHubTreeEntry Entry, byte[] Content)>();
        string? commit = null;
        long size = 0;
        await github.DownloadTarballAsync(repositoryFullName, async archive =>
        {
            await using var tar = new TarReader(await GzipAwareAsync(archive, cancellationToken));
            while (await tar.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                {
                    continue;
                }

                // Every path starts with GitHub's root folder, "{owner}-{repo}-{short commit}/".
                var slash = entry.Name.IndexOf('/');
                if (slash < 0)
                {
                    continue;
                }
                commit ??= entry.Name[..slash].Split('-')[^1];
                var file = new GitHubTreeEntry(entry.Name[(slash + 1)..], "blob", entry.Length);
                if (RepositoryTreeFilter.Filter([file], prefix).Count == 0)
                {
                    continue;
                }

                size += entry.Length;
                if (files.Count >= settings.MaxFiles || size > settings.MaxSize)
                {
                    throw new DomainRuleException(
                        $"The repository has more than {settings.MaxFiles} readable files or {settings.MaxSizeInMegabytes} MB of them. Analyze one folder at a time.");
                }

                using var content = new MemoryStream((int)entry.Length);
                await entry.DataStream.CopyToAsync(content, cancellationToken);
                files.Add((file, content.ToArray()));
            }
        }, cancellationToken);

        if (files.Count == 0)
        {
            throw NoReadableFiles(prefix);
        }

        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = UploadParallelism, CancellationToken = cancellationToken }, async (file, token) =>
        {
            await storage.WriteAsync(runId, FilesFolder + file.Entry.Path, file.Content, token);
        });
        var manifest = new SnapshotManifest(commit, files.Select(f => new SnapshotFile(f.Entry.Path, f.Entry.Size ?? 0)).OrderBy(f => f.Path, StringComparer.Ordinal).ToList());
        await storage.WriteAsync(runId, ManifestName, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken);
        logger.LogInformation("Stored a snapshot of {Repository} ({Commit}) for run {RunId}: {Files} files, {Size} bytes.",
            repositoryFullName, commit, runId, files.Count, size);
        return manifest;
    }

    private async Task<SnapshotManifest?> ReadManifestAsync(Guid runId, CancellationToken cancellationToken)
    {
        var content = await storage.ReadAsync(runId, ManifestName, cancellationToken);
        return content is null ? null : JsonSerializer.Deserialize<SnapshotManifest>(content, JsonOptions);
    }

    private async Task<RepositorySnapshot> ReadFromApiAsync(string repositoryFullName, string? pathPrefix, CancellationToken cancellationToken)
    {
        var tree = await github.GetTreeAsync(repositoryFullName, cancellationToken);
        var files = RepositoryTreeFilter.Filter(tree.Entries, pathPrefix);
        if (files.Count == 0)
        {
            throw NoReadableFiles(RepositoryTreeFilter.NormalizePrefix(pathPrefix));
        }

        var paths = files.Select(file => file.Path).ToList();
        return new RepositorySnapshot(
            paths,
            RepositoryTreeFilter.Outline(files, MaxOutlineLength),
            tree.Truncated,
            maxReads => new RepositoryExplorerTools(paths, async (path, token) => (await github.GetFileAsync(repositoryFullName, path, token))?.Content, maxReads));
    }

    /// <summary>GitHub serves the archive gzipped, unless an HTTP layer already decompressed it; looks at the first bytes to know.</summary>
    private static async Task<Stream> GzipAwareAsync(Stream archive, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        var read = await archive.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var stream = new PrefixedStream(header.AsMemory(0, read), archive);
        return read == 2 && header[0] == 0x1f && header[1] == 0x8b ? new GZipStream(stream, CompressionMode.Decompress) : stream;
    }

    private static DomainRuleException NoReadableFiles(string? prefix)
    {
        return new DomainRuleException(prefix is null
            ? "The repository has no files that can be analyzed."
            : $"The folder '{prefix}' has no files that can be analyzed.");
    }

    private sealed record SnapshotManifest(string? Commit, List<SnapshotFile> Files);

    private sealed record SnapshotFile(string Path, long Size);

    /// <summary>A read-only stream that first returns bytes already read from <paramref name="inner"/>, then the rest of it.</summary>
    private sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
    {
        private ReadOnlyMemory<byte> _prefix = prefix;

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }

        public override long Length
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_prefix.Length > 0)
            {
                var count = Math.Min(buffer.Length, _prefix.Length);
                _prefix.Span[..count].CopyTo(buffer);
                _prefix = _prefix[count..];
                return count;
            }
            return inner.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_prefix.Length > 0)
            {
                var count = Math.Min(buffer.Length, _prefix.Length);
                _prefix[..count].CopyTo(buffer);
                _prefix = _prefix[count..];
                return count;
            }
            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}

/// <param name="Truncated">GitHub truncated the tree, so not every file of a very large repository is known (API mode only).</param>
/// <param name="CreateTools">Creates file tools that can read at most the given number of files.</param>
public sealed record RepositorySnapshot(IReadOnlyList<string> Files, string Outline, bool Truncated, Func<int, RepositoryExplorerTools> CreateTools);
