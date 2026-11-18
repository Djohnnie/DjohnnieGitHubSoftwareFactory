using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>What the worker tells the agent, and what it writes on the branch and pull request. No I/O.</summary>
public static partial class AgentRunPrompt
{
    /// <summary>Appended to the Copilot system prompt: the worker runs unattended and does the git work itself.</summary>
    public const string Instructions = """
        <dghsf_agent_worker>
        - You run unattended in a container job of DGHSF (Djohnnie's GitHub Software Factory). Nobody can answer questions:
          make reasonable decisions yourself and mention them in your final summary.
        - Implement the issue completely. Follow the repository's own instructions files and conventions, and build and run
          the tests when the repository has them.
        - Don't commit, push, create branches or open pull requests: when you finish, DGHSF commits all your changes to the
          current branch and opens the pull request.
        - Only change files inside the working directory. Never print, store or commit secrets or tokens.
        - End with a short summary of what you changed and why, in Markdown without a heading. It becomes the pull request
          description.
        </dghsf_agent_worker>
        """;

    /// <summary>The task for the agent: the issue as DGHSF knows it, with its technical details and parents.</summary>
    /// <param name="ancestors">The items above the item, closest first.</param>
    public static string Build(WorkItem item, IReadOnlyList<WorkItem> ancestors, string branch, string baseBranch)
    {
        var prompt = new StringBuilder();
        prompt.Append(CultureInfo.InvariantCulture,
            $"Implement GitHub issue #{item.IssueNumber} of the repository {item.RepositoryFullName}: a {item.Type.DisplayName.ToLowerInvariant()}. ");
        prompt.Append(CultureInfo.InvariantCulture,
            $"The repository is checked out in the current working directory on the branch `{branch}`, created from `{baseBranch}`.\n\n");
        prompt.Append(CultureInfo.InvariantCulture, $"# {item.Title.Trim()}\n\n{item.Description.Trim()}\n");
        if (!string.IsNullOrWhiteSpace(item.TechnicalDetails))
        {
            prompt.Append(CultureInfo.InvariantCulture, $"\n## Technical details\n\n{item.TechnicalDetails.Trim()}\n");
        }
        if (!string.IsNullOrWhiteSpace(item.CustomInstructions))
        {
            prompt.Append(CultureInfo.InvariantCulture, $"\n## Additional instructions\n\n{item.CustomInstructions.Trim()}\n");
        }

        var parents = ancestors.Where(a => a.IssueNumber is not null).Reverse().ToList();
        if (parents.Count > 0)
        {
            prompt.Append("\n## Part of\n\n");
            foreach (var parent in parents)
            {
                prompt.Append(CultureInfo.InvariantCulture, $"- {parent.Type.DisplayName} #{parent.IssueNumber}: {parent.Title.Trim()}\n");
            }
        }
        return prompt.ToString();
    }

    /// <summary>A new branch per run, e.g. <c>dghsf/42-add-passkey-login-1a2b3c4d</c>.</summary>
    public static string BranchName(WorkItem item, Guid runId)
    {
        var slug = NotSlug().Replace(item.Title.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].TrimEnd('-');
        }
        var prefix = item.IssueNumber is { } number ? number.ToString(CultureInfo.InvariantCulture) : "item";
        return $"dghsf/{prefix}{(slug.Length > 0 ? "-" + slug : string.Empty)}-{runId.ToString("N")[..8]}";
    }

    public static string CommitMessage(WorkItem item, Guid runId)
    {
        return $"{item.Title.Trim()}\n\nCloses #{item.IssueNumber}\n\nImplemented by the DGHSF agent worker (run {runId}).\n";
    }

    /// <summary>The agent's summary, the issue it closes and what the run cost.</summary>
    public static string PullRequestBody(WorkItem item, Guid runId, string? summary, AgentRunUsage usage, string? warning)
    {
        var body = new StringBuilder();
        if (warning is not null)
        {
            body.Append(CultureInfo.InvariantCulture, $"> [!WARNING]\n> {warning}\n\n");
        }
        body.Append(string.IsNullOrWhiteSpace(summary) ? "_The agent didn't write a summary._" : summary.Trim());
        body.Append(CultureInfo.InvariantCulture, $"\n\nCloses #{item.IssueNumber}\n\n---\n\n");
        var credits = AiModel.FormatCredits(usage.NanoAiu / AgentRun.NanoAiuPerCredit);
        var models = string.IsNullOrWhiteSpace(usage.Models) ? "the default model" : usage.Models;
        body.Append(CultureInfo.InvariantCulture,
            $"<sub>🤖 Implemented by the DGHSF agent worker with the GitHub Copilot SDK · {models} · {credits}{(usage.Complete ? string.Empty : " (at least)")} · run `{runId}`</sub>\n");
        return body.ToString();
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();
}
