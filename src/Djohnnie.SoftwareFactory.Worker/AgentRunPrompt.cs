using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
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
          description, or when you revise a pull request, DGHSF's comment on it.
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
        AppendIssue(prompt, item, ancestors);
        return prompt.ToString();
    }

    /// <summary>
    /// The task of a revision run: the feedback to address (with the ids the agent answers it by), the merge of the base
    /// branch, the earlier discussion as context and the issue the pull request implements.
    /// </summary>
    public static string BuildRevision(WorkItem item, IReadOnlyList<WorkItem> ancestors, int pullRequestNumber, string pullRequestTitle, string branch,
        string baseBranch, PullRequestDiscussion discussion, GitMergeResult merge)
    {
        var prompt = new StringBuilder();
        prompt.Append(CultureInfo.InvariantCulture,
            $"Revise pull request #{pullRequestNumber} (\"{pullRequestTitle.Trim()}\") of the repository {item.RepositoryFullName}, which you opened earlier to implement ");
        prompt.Append(CultureInfo.InvariantCulture, $"GitHub issue #{item.IssueNumber}: a {item.Type.DisplayName.ToLowerInvariant()}. ");
        prompt.Append(CultureInfo.InvariantCulture,
            $"The repository is checked out in the current working directory on the pull request's branch `{branch}`, which holds the work so far.\n\n");
        switch (merge.Outcome)
        {
            case GitMergeOutcome.Merged:
                prompt.Append(CultureInfo.InvariantCulture, $"DGHSF merged the latest `{baseBranch}` into the branch, so build and test against it.\n\n");
                break;
            case GitMergeOutcome.Conflicts:
                prompt.Append(CultureInfo.InvariantCulture, $"DGHSF is merging the latest `{baseBranch}` into the branch, which has conflicts in:\n\n");
                foreach (var file in merge.Conflicts)
                {
                    prompt.Append(CultureInfo.InvariantCulture, $"- `{file}`\n");
                }
                prompt.Append("\nResolve every conflict first, keeping what both sides meant, and remove all conflict markers. ");
                prompt.Append("Don't commit or abort the merge: DGHSF concludes it when it commits your changes.\n\n");
                break;
        }

        prompt.Append("Address the new feedback below. For every piece of it, call the `");
        prompt.Append(FeedbackReplies.ToolName);
        prompt.Append("` tool with its id and your answer: what you changed, or why you changed nothing (for example when you only answered a question). ");
        prompt.Append("DGHSF posts the answers on the pull request after it pushed your changes. ");
        prompt.Append("Your final summary is about this revision only.\n\n## New feedback\n");
        foreach (var entry in discussion.Feedback)
        {
            prompt.Append(CultureInfo.InvariantCulture, $"\n### {entry.Id} · {Describe(entry)}\n\n{entry.Body}\n");
        }

        if (discussion.Earlier.Count > 0)
        {
            prompt.Append("\n## Earlier discussion\n\nFor context only: this was handled before.\n");
            foreach (var entry in discussion.Earlier)
            {
                prompt.Append(CultureInfo.InvariantCulture, $"\n### {Describe(entry)}\n\n{entry.Body}\n");
            }
        }

        prompt.Append("\n---\n\nThe issue that the pull request implements:\n\n");
        AppendIssue(prompt, item, ancestors);
        return prompt.ToString();
    }

    private static void AppendIssue(StringBuilder prompt, WorkItem item, IReadOnlyList<WorkItem> ancestors)
    {
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
    }

    /// <summary>Who wrote it and where, e.g. <c>line comment by @alice on `src/App.cs` line 12</c>.</summary>
    private static string Describe(PullRequestDiscussionEntry entry)
    {
        var author = entry.Author is null ? "someone" : $"@{entry.Author}";
        return entry.Kind switch
        {
            PullRequestDiscussionKind.LineComment when entry.Path is not null => entry.Line is { } line
                ? string.Create(CultureInfo.InvariantCulture, $"line comment by {author} on `{entry.Path}` line {line}")
                : $"line comment by {author} on `{entry.Path}` (a line that is no longer in the diff)",
            PullRequestDiscussionKind.LineComment => $"line comment by {author}",
            PullRequestDiscussionKind.Review => $"review by {author}",
            _ => $"comment by {author}"
        };
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

    public static string RevisionCommitMessage(WorkItem item, int pullRequestNumber, Guid runId, string baseBranch, bool merging)
    {
        var title = merging ? $"Merge {baseBranch} and address the feedback on #{pullRequestNumber}" : $"Address the feedback on #{pullRequestNumber}";
        return $"{title}\n\nRefs #{item.IssueNumber}\n\nRevised by the DGHSF agent worker (run {runId}).\n";
    }

    /// <summary>An answer to a line comment, posted in its thread.</summary>
    public static string ReplyBody(Guid runId, string reply)
    {
        return $"{reply.Trim()}\n\n{PullRequestDiscussion.Marker(runId)}";
    }

    /// <summary>
    /// DGHSF's comment on a revised pull request: the agent's summary, its answers to the feedback that isn't on a line
    /// (line comments get theirs in their thread), what was pushed and merged, and what the revision cost.
    /// </summary>
    public static string RevisionComment(Guid runId, string? summary, AgentRunUsage usage, string? warning, PullRequestDiscussion discussion,
        FeedbackReplies replies, bool pushed, string branch, string baseBranch, GitMergeResult merge)
    {
        var body = new StringBuilder();
        if (warning is not null)
        {
            body.Append(CultureInfo.InvariantCulture, $"> [!WARNING]\n> {warning}\n\n");
        }
        body.Append(string.IsNullOrWhiteSpace(summary) ? "_The agent didn't write a summary._" : summary.Trim());
        body.Append("\n\n");

        var answered = discussion.Feedback.Where(f => f.Kind != PullRequestDiscussionKind.LineComment).ToList();
        if (answered.Count > 0)
        {
            body.Append("#### Answers\n");
            foreach (var entry in answered)
            {
                var author = entry.Author is null ? string.Empty : $" @{entry.Author}";
                var kind = entry.Kind == PullRequestDiscussionKind.Review ? "review" : "comment";
                body.Append(CultureInfo.InvariantCulture, $"\n{Quote(entry.Body)}\n> —{author} ([{kind}]({entry.Url}))\n\n");
                body.Append(replies.ReplyTo(entry.Id!) ?? "_The agent didn't answer this._");
                body.Append('\n');
            }
            body.Append('\n');
        }
        var unanswered = discussion.Feedback.Where(f => f.Kind == PullRequestDiscussionKind.LineComment && replies.ReplyTo(f.Id!) is null).ToList();
        if (unanswered.Count > 0)
        {
            body.Append("The agent didn't answer these line comments: ");
            body.Append(string.Join(", ", unanswered.Select(f => f.Line is { } line
                ? string.Create(CultureInfo.InvariantCulture, $"[{f.Path}:{line}]({f.Url})")
                : $"[{f.Path}]({f.Url})")));
            body.Append(".\n\n");
        }

        body.Append(pushed ? $"Pushed the changes to `{branch}`." : "Nothing changed, so nothing was pushed.");
        switch (merge.Outcome)
        {
            case GitMergeOutcome.Merged:
                body.Append(CultureInfo.InvariantCulture, $" Merged the latest `{baseBranch}` into the branch.");
                break;
            case GitMergeOutcome.Conflicts:
                body.Append(CultureInfo.InvariantCulture,
                    $" Merged the latest `{baseBranch}` into the branch and resolved the conflicts in {string.Join(", ", merge.Conflicts.Select(f => $"`{f}`"))}.");
                break;
        }
        body.Append("\n\n---\n\n");
        body.Append(Footer("Revised", usage, runId));
        body.Append(PullRequestDiscussion.Marker(runId));
        body.Append('\n');
        return body.ToString();
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
        body.Append(Footer("Implemented", usage, runId));
        return body.ToString();
    }

    private static string Footer(string verb, AgentRunUsage usage, Guid runId)
    {
        var credits = AiModel.FormatCredits(usage.NanoAiu / AgentRun.NanoAiuPerCredit);
        var models = string.IsNullOrWhiteSpace(usage.Models) ? "the default model" : usage.Models;
        return string.Create(CultureInfo.InvariantCulture,
            $"<sub>🤖 {verb} by the DGHSF agent worker with the GitHub Copilot SDK · {models} · {credits}{(usage.Complete ? string.Empty : " (at least)")} · run `{runId}`</sub>\n");
    }

    /// <summary>The start of the text as a Markdown quote.</summary>
    private static string Quote(string text)
    {
        var shortened = text.Length <= 300 ? text : $"{text[..300]}…";
        return string.Join('\n', shortened.ReplaceLineEndings("\n").Split('\n').Select(line => $"> {line}"));
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();
}
