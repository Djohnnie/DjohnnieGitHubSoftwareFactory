using System.Globalization;
using System.Text;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>
/// Picks the files of a repository tree that are worth reading for an analysis (no binaries, dependencies, build output,
/// lock files or generated code) and writes them as a compact outline for the agents.
/// </summary>
public static class RepositoryTreeFilter
{
    /// <summary>Larger files are left out: they are almost always data or generated.</summary>
    public const long MaxFileSize = 200_000;

    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", ".nuget", ".gradle", ".next", ".nuxt", ".terraform", ".venv", "venv", "__pycache__",
        "node_modules", "bower_components", "jspm_packages", "vendor", "packages", "bin", "obj", "dist", "build", "out", "target",
        "coverage", "TestResults", "artifacts"
    };

    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg", ".tif", ".tiff", ".psd",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".gz", ".tgz", ".tar", ".7z", ".rar", ".nupkg", ".jar", ".war",
        ".dll", ".exe", ".pdb", ".so", ".dylib", ".a", ".lib", ".o", ".class", ".pyc", ".wasm",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".mp3", ".mp4", ".wav", ".ogg", ".mov", ".avi", ".webm",
        ".snk", ".pfx", ".p12", ".cer", ".keystore", ".map", ".bak", ".db", ".sqlite", ".mdf", ".ldf"
    };

    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "npm-shrinkwrap.json", "packages.lock.json", "poetry.lock",
        "Pipfile.lock", "Cargo.lock", "composer.lock", "Gemfile.lock", "go.sum", "mix.lock", "pubspec.lock", ".DS_Store"
    };

    private static readonly string[] GeneratedSuffixes =
    [
        ".min.js", ".min.css", ".Designer.cs", ".g.cs", ".g.i.cs", ".generated.cs", "ModelSnapshot.cs", ".pb.go", "_pb2.py"
    ];

    /// <summary>A folder prefix without slashes at the ends; <c>null</c> for the whole repository.</summary>
    public static string? NormalizePrefix(string? prefix)
    {
        var segments = (prefix ?? string.Empty).Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment != ".")
            .ToList();
        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    /// <summary><c>false</c> for a prefix with '..', which could leave the repository's API paths.</summary>
    public static bool IsValidPrefix(string? prefix)
    {
        return !(prefix ?? string.Empty).Replace('\\', '/').Split('/').Any(segment => segment.Trim() == "..");
    }

    /// <summary>The files worth reading, within <paramref name="pathPrefix"/> when given, in path order.</summary>
    public static IReadOnlyList<GitHubTreeEntry> Filter(IEnumerable<GitHubTreeEntry> entries, string? pathPrefix)
    {
        var prefix = NormalizePrefix(pathPrefix);
        return entries
            .Where(entry => entry.IsFile && IsInFolder(entry.Path, prefix) && IsReadable(entry))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
    }

    public static bool IsReadable(GitHubTreeEntry entry)
    {
        if (entry.Size is > MaxFileSize)
        {
            return false;
        }

        var segments = entry.Path.Split('/');
        var name = segments[^1];
        return !segments[..^1].Any(ExcludedFolders.Contains)
            && !ExcludedFiles.Contains(name)
            && !ExcludedExtensions.Contains(Path.GetExtension(name))
            && !GeneratedSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// One line per file with its size. When that is longer than <paramref name="maxLength"/>, it lists every folder with
    /// its number of files and total size instead, plus the files in the root (the agent lists folders to see more).
    /// </summary>
    public static string Outline(IReadOnlyList<GitHubTreeEntry> files, int maxLength)
    {
        var builder = new StringBuilder();
        foreach (var file in files)
        {
            builder.Append(file.Path).Append("  (").Append(Size(file.Size ?? 0)).AppendLine(")");
        }
        if (builder.Length <= maxLength)
        {
            return builder.ToString().TrimEnd();
        }

        builder.Clear();
        builder.AppendLine($"[{files.Count} files; too many to list them all. Folders with their number of files and size, then the files in the root.]");
        var folders = files
            .Where(file => file.Path.Contains('/'))
            .SelectMany(file => Ancestors(file.Path).Select(folder => (Folder: folder, Size: file.Size ?? 0)))
            .GroupBy(item => item.Folder, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            builder.Append(folder.Key).Append("/  (").Append(folder.Count()).Append(" files, ").Append(Size(folder.Sum(item => item.Size))).AppendLine(")");
        }
        foreach (var file in files.Where(file => !file.Path.Contains('/')))
        {
            builder.Append(file.Path).Append("  (").Append(Size(file.Size ?? 0)).AppendLine(")");
        }
        return builder.ToString().TrimEnd();
    }

    private static bool IsInFolder(string path, string? prefix)
    {
        return prefix is null || path.StartsWith(prefix + "/", StringComparison.Ordinal) || path == prefix;
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        var slash = path.IndexOf('/');
        while (slash > 0)
        {
            yield return path[..slash];
            slash = path.IndexOf('/', slash + 1);
        }
    }

    private static string Size(long bytes)
    {
        return bytes < 1024
            ? $"{bytes} B"
            : bytes < 1024 * 1024
                ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.#} KB")
                : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0.#} MB");
    }
}
