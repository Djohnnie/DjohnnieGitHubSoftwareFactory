using System.ComponentModel;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Read-only tools that let a chat agent list and read the files of the selected repository on its default branch,
/// live from GitHub. Long files are cut off; paths with <c>..</c> are refused.
/// </summary>
/// <param name="listDirectory">Lists a directory of the repository (empty path = root) on the default branch.</param>
/// <param name="readFile">Reads a file of the repository on the default branch, or <c>null</c> when it does not exist.</param>
public sealed class RepositoryFileTools(
    Func<string, CancellationToken, Task<IReadOnlyList<RepositoryEntry>>> listDirectory,
    Func<string, CancellationToken, Task<string?>> readFile)
{
    /// <summary>Files longer than this are cut off, so a single file doesn't flood the conversation.</summary>
    public const int MaxFileLength = 20_000;

    public const string ListToolName = "list_repository_files";
    public const string ReadToolName = "read_repository_file";

    public IList<AITool> AsTools()
    {
        return
        [
            AIFunctionFactory.Create(ListRepositoryFilesAsync, ListToolName),
            AIFunctionFactory.Create(ReadRepositoryFileAsync, ReadToolName)
        ];
    }

    [Description("Lists the files and folders of a directory in the selected repository (default branch).")]
    public async Task<string> ListRepositoryFilesAsync(
        [Description("The directory path relative to the repository root, e.g. 'src' or '.github/workflows'. Empty for the root.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizePath(path, out var directory))
        {
            return OutsideRepository;
        }

        try
        {
            var entries = await listDirectory(directory, cancellationToken);
            if (entries.Count == 0)
            {
                return $"'{DisplayPath(directory)}' is empty or does not exist.";
            }
            return string.Join("\n", entries
                .OrderBy(e => e.Type == "dir" ? 0 : 1)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.Type == "dir" ? $"{e.Path}/" : e.Path));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Could not list '{DisplayPath(directory)}': {ex.Message}";
        }
    }

    [Description("Reads a text file of the selected repository (default branch). Long files are cut off.")]
    public async Task<string> ReadRepositoryFileAsync(
        [Description("The file path relative to the repository root, e.g. 'README.md' or 'src/App/App.csproj'.")] string path,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizePath(path, out var file))
        {
            return OutsideRepository;
        }
        if (file.Length == 0)
        {
            return $"Pass the path of a file; use {ListToolName} to list a directory.";
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

    internal const string OutsideRepository = "Paths are relative to the repository root and can't contain '..'.";

    /// <summary>Normalizes a repository path; <c>false</c> for paths with '..', which could reach other GitHub API endpoints.</summary>
    internal static bool TryNormalizePath(string? path, out string normalized)
    {
        var segments = (path ?? string.Empty).Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment != ".")
            .ToList();
        normalized = string.Join('/', segments);
        return !segments.Contains("..");
    }

    private static string DisplayPath(string path)
    {
        return path.Length == 0 ? "/" : path;
    }
}
