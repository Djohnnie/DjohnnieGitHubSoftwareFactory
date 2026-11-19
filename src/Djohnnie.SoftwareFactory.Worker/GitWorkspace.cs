using System.Diagnostics;
using System.Text;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>The git commands of a run, run with the git command line in the run's own folder.</summary>
public interface IGitWorkspace
{
    /// <summary>A shallow clone of <paramref name="branch"/> into <paramref name="directory"/>, with a new branch <paramref name="newBranch"/> checked out.</summary>
    Task CloneAsync(string repositoryFullName, string branch, string newBranch, string directory, string token, CancellationToken cancellationToken);

    /// <summary>
    /// A clone of an existing <paramref name="branch"/> into <paramref name="directory"/>, checked out, with
    /// <paramref name="baseBranch"/> as <c>origin/{baseBranch}</c> and enough history of both to merge them.
    /// </summary>
    /// <returns>The commit the branch is at.</returns>
    Task<string> CloneBranchAsync(string repositoryFullName, string branch, string baseBranch, string directory, string token, CancellationToken cancellationToken);

    /// <summary>
    /// Merges <c>origin/{baseBranch}</c> into the checked out branch. A merge without conflicts is committed; with
    /// conflicts the merge stays in progress (with conflict markers in the files) and <see cref="CommitAllAsync"/> concludes it.
    /// </summary>
    Task<GitMergeResult> MergeBaseAsync(string directory, string baseBranch, string authorName, string authorEmail, CancellationToken cancellationToken);

    /// <summary>Whether the agent changed anything: files that aren't committed, or commits on top of <paramref name="since"/> (a branch or a commit).</summary>
    Task<bool> HasChangesAsync(string directory, string since, CancellationToken cancellationToken);

    /// <summary>Commits every change (if any is left uncommitted, or a merge is in progress) with the configured author.</summary>
    Task CommitAllAsync(string directory, string message, string authorName, string authorEmail, CancellationToken cancellationToken);

    Task PushAsync(string directory, string branch, string token, CancellationToken cancellationToken);
}

public enum GitMergeOutcome
{
    /// <summary>The branch already contains the base branch.</summary>
    UpToDate = 0,

    /// <summary>The base branch was merged and committed.</summary>
    Merged = 1,

    /// <summary>The merge is in progress with conflicts in <see cref="GitMergeResult.Conflicts"/>.</summary>
    Conflicts = 2
}

/// <param name="Conflicts">The files with conflicts, relative to the repository and with forward slashes.</param>
public sealed record GitMergeResult(GitMergeOutcome Outcome, IReadOnlyList<string> Conflicts)
{
    public static GitMergeResult UpToDate { get; } = new(GitMergeOutcome.UpToDate, []);
}

/// <remarks>
/// The token never ends up in the clone's configuration or on a command line: it goes to git as an
/// <c>http.extraHeader</c> through <c>GIT_CONFIG_*</c> environment variables of the clone, fetch and push commands only.
/// The agent's own shell commands therefore can't push.
/// </remarks>
public sealed class GitWorkspace : IGitWorkspace
{
    private const int Depth = 50;

    /// <summary>How often the history is deepened to find where the branch and the base branch meet, before fetching all of it.</summary>
    private const int MaxDeepen = 3;

    public async Task CloneAsync(string repositoryFullName, string branch, string newBranch, string directory, string token, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        await RunAsync(null, token, cancellationToken, "clone", "--depth", $"{Depth}", "--single-branch", "--no-tags", "--branch", branch,
            $"https://github.com/{repositoryFullName}.git", directory);
        await RunAsync(directory, null, cancellationToken, "checkout", "-b", newBranch);
    }

    public async Task<string> CloneBranchAsync(string repositoryFullName, string branch, string baseBranch, string directory, string token, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        await RunAsync(null, token, cancellationToken, "clone", "--depth", $"{Depth}", "--single-branch", "--no-tags", "--branch", branch,
            $"https://github.com/{repositoryFullName}.git", directory);
        string[] refspecs = [$"+refs/heads/{branch}:refs/remotes/origin/{branch}", $"+refs/heads/{baseBranch}:refs/remotes/origin/{baseBranch}"];
        await RunAsync(directory, token, cancellationToken, ["fetch", "--depth", $"{Depth}", "--no-tags", "origin", .. refspecs]);

        // A merge needs the commit where both histories meet; shallow clones may not reach it yet. Without one at all (the
        // whole history is there), the merge itself says why it can't merge.
        for (var attempt = 0; !await HasMergeBaseAsync(directory, baseBranch, cancellationToken); attempt++)
        {
            if ((await RunAsync(directory, null, cancellationToken, "rev-parse", "--is-shallow-repository")).Trim() != "true")
            {
                break;
            }
            string[] deepen = attempt < MaxDeepen ? ["--deepen", $"{Depth * 4}"] : ["--unshallow"];
            await RunAsync(directory, token, cancellationToken, ["fetch", .. deepen, "--no-tags", "origin", .. refspecs]);
        }
        return (await RunAsync(directory, null, cancellationToken, "rev-parse", "HEAD")).Trim();
    }

    public async Task<GitMergeResult> MergeBaseAsync(string directory, string baseBranch, string authorName, string authorEmail, CancellationToken cancellationToken)
    {
        var before = (await RunAsync(directory, null, cancellationToken, "rev-parse", "HEAD")).Trim();
        var merge = await TryRunAsync(directory, null, cancellationToken, "-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}",
            "merge", "--no-edit", "--no-verify", $"origin/{baseBranch}");
        var conflicts = (await RunAsync(directory, null, cancellationToken, "diff", "--name-only", "--diff-filter=U"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (conflicts.Length > 0)
        {
            return new(GitMergeOutcome.Conflicts, conflicts);
        }
        if (merge.ExitCode != 0)
        {
            throw new GitCommandException($"git merge failed ({merge.ExitCode}): {merge.Error.Trim()}");
        }
        var after = (await RunAsync(directory, null, cancellationToken, "rev-parse", "HEAD")).Trim();
        return after == before ? GitMergeResult.UpToDate : new(GitMergeOutcome.Merged, []);
    }

    public async Task<bool> HasChangesAsync(string directory, string since, CancellationToken cancellationToken)
    {
        var status = await RunAsync(directory, null, cancellationToken, "status", "--porcelain");
        if (!string.IsNullOrWhiteSpace(status))
        {
            return true;
        }
        var ahead = await RunAsync(directory, null, cancellationToken, "rev-list", "--count", "HEAD", $"^{since}");
        return int.TryParse(ahead.Trim(), out var count) && count > 0;
    }

    public async Task CommitAllAsync(string directory, string message, string authorName, string authorEmail, CancellationToken cancellationToken)
    {
        await RunAsync(directory, null, cancellationToken, "add", "--all");
        var staged = await RunAsync(directory, null, cancellationToken, "diff", "--cached", "--name-only");
        var merging = (await TryRunAsync(directory, null, cancellationToken, "rev-parse", "-q", "--verify", "MERGE_HEAD")).ExitCode == 0;
        if (string.IsNullOrWhiteSpace(staged) && !merging)
        {
            return;
        }
        await RunAsync(directory, null, cancellationToken, "-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}",
            "commit", "--no-verify", "--message", message);
    }

    public async Task PushAsync(string directory, string branch, string token, CancellationToken cancellationToken)
    {
        await RunAsync(directory, token, cancellationToken, "push", "--no-verify", "origin", $"HEAD:refs/heads/{branch}");
    }

    private static async Task<bool> HasMergeBaseAsync(string directory, string baseBranch, CancellationToken cancellationToken)
    {
        return (await TryRunAsync(directory, null, cancellationToken, "merge-base", "HEAD", $"origin/{baseBranch}")).ExitCode == 0;
    }

    private static async Task<string> RunAsync(string? directory, string? token, CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await TryRunAsync(directory, token, cancellationToken, arguments);
        if (result.ExitCode != 0)
        {
            throw new GitCommandException($"git {arguments.FirstOrDefault(a => !a.StartsWith('-') && !a.Contains('='))} failed ({result.ExitCode}): {result.Error.Trim()}");
        }
        return result.Output;
    }

    private static async Task<(int ExitCode, string Output, string Error)> TryRunAsync(string? directory, string? token, CancellationToken cancellationToken,
        params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (token is not null)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
            start.Environment["GIT_CONFIG_COUNT"] = "1";
            start.Environment["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader";
            start.Environment["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {basic}";
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await output, await error);
    }
}

public sealed class GitCommandException(string message) : Exception(message);
