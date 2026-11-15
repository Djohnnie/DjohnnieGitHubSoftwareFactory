using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Read-only tools that let a background agent explore one repository. Directories are listed from the file tree that
/// was read up front (no GitHub requests); only the files in that tree can be read, at most <paramref name="maxReads"/>
/// of them, so one agent run can't read the whole repository or exhaust the token's rate limit.
/// </summary>
/// <param name="files">The paths of the files that may be read (the filtered tree), relative to the repository root.</param>
/// <param name="readFile">Reads a file on the analyzed branch, or <c>null</c> when it does not exist.</param>
/// <param name="maxReads">How many files the agent may read.</param>
public sealed class RepositoryExplorerTools(
    IReadOnlyCollection<string> files,
    Func<string, CancellationToken, Task<string?>> readFile,
    int maxReads = RepositoryExplorerTools.DefaultMaxReads)
{
    public const int DefaultMaxReads = 30;

    /// <summary>Files longer than this are cut off, so a single file doesn't flood the conversation.</summary>
    public const int MaxFileLength = 16_000;

    private const int MaxListedEntries = 300;

    private readonly HashSet<string> _files = files.ToHashSet(StringComparer.Ordinal);
    private int _reads;

    /// <summary>How many files the agent read.</summary>
    public int Reads
    {
        get
        {
            return Volatile.Read(ref _reads);
        }
    }

    public IList<AITool> AsTools()
    {
        return
        [
            AIFunctionFactory.Create(ListFiles, "list_repository_files"),
            AIFunctionFactory.Create(ReadFileAsync, "read_repository_file")
        ];
    }

    [Description("Lists the files and folders of a directory of the repository. Only files that can be read are listed.")]
    public string ListFiles(
        [Description("The directory path relative to the repository root, e.g. 'src' or '.github/workflows'. Empty for the root.")] string? path = null)
    {
        if (!CopilotInstructionsFormTools.TryNormalizePath(path, out var directory))
        {
            return CopilotInstructionsFormTools.OutsideRepository;
        }

        var prefix = directory.Length == 0 ? string.Empty : directory + "/";
        var entries = _files
            .Where(file => file.StartsWith(prefix, StringComparison.Ordinal))
            .Select(file =>
            {
                var rest = file[prefix.Length..];
                var slash = rest.IndexOf('/');
                return slash < 0 ? prefix + rest : prefix + rest[..slash] + "/";
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry.EndsWith('/') ? 0 : 1)
            .ThenBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (entries.Count == 0)
        {
            return $"'{(directory.Length == 0 ? "/" : directory)}' has no readable files or does not exist.";
        }

        var listed = string.Join("\n", entries.Take(MaxListedEntries));
        return entries.Count <= MaxListedEntries ? listed : $"{listed}\n[{entries.Count - MaxListedEntries} more entries not shown; list a subfolder.]";
    }

    [Description("Reads a text file of the repository. Long files are cut off, and only a limited number of files can be read: choose the ones that matter most.")]
    public async Task<string> ReadFileAsync(
        [Description("The file path relative to the repository root, e.g. 'README.md' or 'src/App/App.csproj'.")] string path,
        CancellationToken cancellationToken = default)
    {
        if (!CopilotInstructionsFormTools.TryNormalizePath(path, out var file))
        {
            return CopilotInstructionsFormTools.OutsideRepository;
        }
        if (!_files.Contains(file))
        {
            return $"'{file}' can't be read: it doesn't exist, it is a folder, or it was left out (binary, generated, vendored or too large).";
        }
        if (Interlocked.Increment(ref _reads) > maxReads)
        {
            return $"The limit of {maxReads} files is reached; write your answer with what you have read.";
        }

        try
        {
            var content = await readFile(file, cancellationToken);
            if (content is null)
            {
                return $"'{file}' does not exist.";
            }
            if (content.Contains('\0'))
            {
                return $"'{file}' is a binary file.";
            }
            return content.Length <= MaxFileLength
                ? content
                : $"{content[..MaxFileLength]}\n\n[Cut off: the file has {content.Length} characters; only the first {MaxFileLength} are shown.]";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Could not read '{file}': {ex.Message}";
        }
    }
}
