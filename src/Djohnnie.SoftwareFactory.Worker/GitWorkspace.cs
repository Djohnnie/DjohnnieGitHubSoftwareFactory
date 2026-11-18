using System.Diagnostics;
using System.Text;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>The git commands of a run, run with the git command line in the run's own folder.</summary>
public interface IGitWorkspace
{
    /// <summary>A shallow clone of <paramref name="branch"/> into <paramref name="directory"/>, with a new branch <paramref name="newBranch"/> checked out.</summary>
    Task CloneAsync(string repositoryFullName, string branch, string newBranch, string directory, string token, CancellationToken cancellationToken);

    /// <summary>Whether the agent changed anything: files that aren't committed, or commits on top of <paramref name="baseBranch"/>.</summary>
    Task<bool> HasChangesAsync(string directory, string baseBranch, CancellationToken cancellationToken);

    /// <summary>Commits every change (if any is left uncommitted) with the configured author.</summary>
    Task CommitAllAsync(string directory, string message, string authorName, string authorEmail, CancellationToken cancellationToken);

    Task PushAsync(string directory, string branch, string token, CancellationToken cancellationToken);
}

/// <remarks>
/// The token never ends up in the clone's configuration or on a command line: it goes to git as an
/// <c>http.extraHeader</c> through <c>GIT_CONFIG_*</c> environment variables of the clone and push commands only. The agent's
/// own shell commands therefore can't push.
/// </remarks>
public sealed class GitWorkspace : IGitWorkspace
{
    public async Task CloneAsync(string repositoryFullName, string branch, string newBranch, string directory, string token, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        await RunAsync(null, token, cancellationToken, "clone", "--depth", "50", "--single-branch", "--no-tags", "--branch", branch,
            $"https://github.com/{repositoryFullName}.git", directory);
        await RunAsync(directory, null, cancellationToken, "checkout", "-b", newBranch);
    }

    public async Task<bool> HasChangesAsync(string directory, string baseBranch, CancellationToken cancellationToken)
    {
        var status = await RunAsync(directory, null, cancellationToken, "status", "--porcelain");
        if (!string.IsNullOrWhiteSpace(status))
        {
            return true;
        }
        var ahead = await RunAsync(directory, null, cancellationToken, "rev-list", "--count", "HEAD", $"^origin/{baseBranch}");
        return int.TryParse(ahead.Trim(), out var count) && count > 0;
    }

    public async Task CommitAllAsync(string directory, string message, string authorName, string authorEmail, CancellationToken cancellationToken)
    {
        await RunAsync(directory, null, cancellationToken, "add", "--all");
        var staged = await RunAsync(directory, null, cancellationToken, "diff", "--cached", "--name-only");
        if (string.IsNullOrWhiteSpace(staged))
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

    private static async Task<string> RunAsync(string? directory, string? token, CancellationToken cancellationToken, params string[] arguments)
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
        if (process.ExitCode != 0)
        {
            throw new GitCommandException($"git {arguments.FirstOrDefault(a => !a.StartsWith('-') && !a.Contains('='))} failed ({process.ExitCode}): {(await error).Trim()}");
        }
        return await output;
    }
}

public sealed class GitCommandException(string message) : Exception(message);
