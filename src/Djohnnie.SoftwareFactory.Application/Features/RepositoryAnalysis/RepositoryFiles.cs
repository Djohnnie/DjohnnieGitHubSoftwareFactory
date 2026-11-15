using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;

namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>Reads the file tree of a repository once and gives the analysis agents an outline and file tools.</summary>
public sealed class RepositoryFiles(GitHubApiClient github)
{
    public const int MaxOutlineLength = 40_000;

    /// <exception cref="DomainRuleException">The repository (folder) has no readable files.</exception>
    public async Task<RepositorySnapshot> ReadAsync(string repositoryFullName, string? pathPrefix, CancellationToken cancellationToken)
    {
        var tree = await github.GetTreeAsync(repositoryFullName, cancellationToken);
        var files = RepositoryTreeFilter.Filter(tree.Entries, pathPrefix);
        if (files.Count == 0)
        {
            throw new DomainRuleException(pathPrefix is null
                ? "The repository has no files that can be analyzed."
                : $"The folder '{pathPrefix}' has no files that can be analyzed.");
        }

        var paths = files.Select(file => file.Path).ToList();
        return new RepositorySnapshot(
            paths,
            RepositoryTreeFilter.Outline(files, MaxOutlineLength),
            tree.Truncated,
            maxReads => new RepositoryExplorerTools(paths, async (path, token) => (await github.GetFileAsync(repositoryFullName, path, token))?.Content, maxReads));
    }
}

/// <param name="Truncated">GitHub truncated the tree, so not every file of a very large repository is known.</param>
/// <param name="CreateTools">Creates file tools that can read at most the given number of files.</param>
public sealed record RepositorySnapshot(IReadOnlyList<string> Files, string Outline, bool Truncated, Func<int, RepositoryExplorerTools> CreateTools);
