using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>Says in words what a refresh from GitHub changed on a work item, for the sync log.</summary>
public static class WorkItemChanges
{
    public static string Describe(WorkItem before, WorkItem after)
    {
        var changes = new List<string>();
        if (before.Status != after.Status)
        {
            changes.Add($"status {LabelOf(before.Status)} → {LabelOf(after.Status)}");
        }
        if (before.Progress != after.Progress)
        {
            changes.Add($"Copilot {LabelOf(before.Progress)} → {LabelOf(after.Progress)}");
        }
        if (before.PullRequestNumber != after.PullRequestNumber)
        {
            changes.Add(after.PullRequestNumber is { } number ? $"pull request #{number}" : "no pull request any more");
        }
        if (before.PullRequestChecks != after.PullRequestChecks)
        {
            changes.Add($"checks {before.PullRequestChecks} → {after.PullRequestChecks}");
        }
        if (before.AgentRunUrl != after.AgentRunUrl && after.AgentRunUrl is not null)
        {
            changes.Add("new agent run");
        }
        if (before.Title != after.Title)
        {
            changes.Add($"title '{before.Title}' → '{after.Title}'");
        }
        if (before.Description != after.Description)
        {
            changes.Add("description updated");
        }
        if (changes.Count == 0)
        {
            // E.g. the issue URL after a repository rename, or the pull request's branch.
            changes.Add("GitHub details updated");
        }

        return $"{after.Type.DisplayName} '{after.Title}': {string.Join("; ", changes)}.";
    }

    private static string LabelOf(WorkItemStatus status)
    {
        return status switch
        {
            WorkItemStatus.InProgress => "In progress",
            WorkItemStatus.Done => "Closed",
            _ => status.ToString()
        };
    }

    private static string LabelOf(CopilotProgress progress)
    {
        return progress switch
        {
            CopilotProgress.ReadyForReview => "Ready for review",
            CopilotProgress.ChangesRequested => "Changes requested",
            _ => progress.ToString()
        };
    }
}
