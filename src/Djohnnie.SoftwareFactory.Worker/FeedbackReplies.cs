using System.Collections.Concurrent;
using System.ComponentModel;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>
/// The agent's answers to the feedback of a revision run, which it gives with the <see cref="ToolName"/> tool while it works.
/// Nothing is posted then: the worker posts the answers on the pull request after it pushed the changes they are about.
/// </summary>
public sealed class FeedbackReplies(IReadOnlyList<PullRequestDiscussionEntry> feedback)
{
    public const string ToolName = "reply_to_feedback";
    public const int MaxReplyLength = 10_000;

    private readonly ConcurrentDictionary<string, string> _replies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The answer per feedback id (<c>F1</c>, …); a later answer to the same feedback replaces the earlier one.</summary>
    public IReadOnlyDictionary<string, string> Replies
    {
        get
        {
            return _replies;
        }
    }

    public AIFunction CreateTool()
    {
        return AIFunctionFactory.Create(Reply, ToolName,
            "Records your answer to one piece of the pull request's feedback (F1, F2, …), which DGHSF posts on the pull request after it pushed your changes: "
            + "a line comment gets it as a reply in its thread, other feedback in DGHSF's comment. Say briefly what you changed for it, or why you changed "
            + "nothing (e.g. you answered a question or disagree). Call it once per feedback id; calling it again replaces the answer.");
    }

    public string? ReplyTo(string id)
    {
        return _replies.GetValueOrDefault(id);
    }

    private string Reply(
        [Description("The id of the feedback, e.g. F1.")] string feedbackId,
        [Description("Your answer in Markdown, addressed to the reviewer.")] string reply)
    {
        var id = feedbackId?.Trim() ?? string.Empty;
        if (!feedback.Any(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return $"There is no feedback '{id}'. The feedback ids are: {string.Join(", ", feedback.Select(f => f.Id))}.";
        }
        if (string.IsNullOrWhiteSpace(reply))
        {
            return "The answer is empty; write what you did about the feedback.";
        }

        var text = reply.Trim();
        _replies[id] = text.Length <= MaxReplyLength ? text : text[..MaxReplyLength];
        var open = feedback.Where(f => f.Id is not null && !_replies.ContainsKey(f.Id)).Select(f => f.Id).ToList();
        return open.Count == 0
            ? $"Saved the answer to {id}. Every piece of feedback has an answer."
            : $"Saved the answer to {id}. Still without an answer: {string.Join(", ", open)}.";
    }
}
