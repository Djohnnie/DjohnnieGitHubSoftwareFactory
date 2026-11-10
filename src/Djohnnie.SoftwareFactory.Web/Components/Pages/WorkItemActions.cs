using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Pages;

/// <summary>Confirmations and feedback shared by the work item lists and the work item editor.</summary>
internal static class WorkItemActions
{
    public static bool IsExpected(Exception ex)
    {
        return ex is Djohnnie.SoftwareFactory.Contracts.DomainRuleException or Djohnnie.SoftwareFactory.Contracts.ValidationFailedException or GitHubApiException or HttpRequestException
            or Djohnnie.SoftwareFactory.Contracts.PersistenceException;
    }

    public static string Name(WorkItemType type)
    {
        return type.DisplayName.ToLowerInvariant();
    }

    public static string AcceptText(WorkItemType type)
    {
        return type.CanBeAssigned ? "Accept & assign" : "Accept & create issue";
    }

    /// <summary>Why the item can't be accepted yet because of its parent, or <c>null</c> when the parent doesn't stand in the way.</summary>
    public static string? AcceptBlockedByParent(WorkItemType type, WorkItemReference? parent)
    {
        return parent is { IssueNumber: null }
            ? $"Accept the {Name(parent.Type)} '{parent.Title}' before accepting this {Name(type)}."
            : null;
    }

    /// <param name="parent">The current parent, to explain that the issue becomes a sub-issue.</param>
    public static Task<bool?> ConfirmAcceptAsync(IDialogService dialogs, WorkItem item, WorkItemReference? parent, string repositoryFullName)
    {
        var name = Name(item.Type);
        var subIssue = parent is { IssueNumber: { } number } ? $" as a sub-issue of #{number} ({parent.Title})" : string.Empty;
        var assignment = string.Empty;
        if (item.Type.CanBeAssigned)
        {
            var agent = string.IsNullOrEmpty(item.CustomAgent) ? "the default Copilot agent" : $"the custom agent '{item.CustomAgent}'";
            var model = string.IsNullOrEmpty(item.Model) ? "the default model" : $"model '{item.Model}'";
            assignment = $" Then the issue is assigned to the Copilot cloud agent using {agent} and {model}.";
        }
        return dialogs.ShowMessageBoxAsync($"Accept {name}",
            $"This creates a GitHub issue '{item.Title}' labelled '{item.Type.Label}' in {repositoryFullName}{subIssue}.{assignment} " +
            $"Afterwards the {name} content can no longer be edited here.",
            yesText: AcceptText(item.Type), cancelText: "Cancel");
    }

    public static Task<bool?> ConfirmDeleteAsync(IDialogService dialogs, WorkItem item)
    {
        return dialogs.ShowMessageBoxAsync($"Delete {Name(item.Type)}",
            item.IssueNumber is { } number
                ? $"Delete '{item.Title}' from DGHSF? GitHub issue #{number} is not touched; close it on GitHub if needed."
                : $"Delete the draft '{item.Title}'? This cannot be undone.",
            yesText: "Delete", cancelText: "Cancel");
    }

    public const string RefreshText = "Refresh from GitHub";

    public static void Report(ISnackbar snackbar, WorkItemRefreshResult result)
    {
        var item = result.WorkItem;
        if (result.Changed)
        {
            snackbar.Add($"Updated '{item.Title}' from issue #{item.IssueNumber}.", Severity.Success);
        }
        else
        {
            snackbar.Add($"'{item.Title}' is up to date with issue #{item.IssueNumber}.", Severity.Info);
        }
    }

    public static void Report(ISnackbar snackbar, WorkItemAcceptResult result)
    {
        var issue = $"#{result.WorkItem.IssueNumber}";
        if (result.HierarchyError is not null)
        {
            snackbar.Add($"Issue {issue} exists, but linking sub-issues failed: {result.HierarchyError}", Severity.Warning,
                o => o.RequireInteraction = true);
        }

        if (!result.WorkItem.Type.CanBeAssigned)
        {
            snackbar.Add($"Created issue {issue}.", Severity.Success);
        }
        else if (result.AssignmentError is null)
        {
            snackbar.Add($"Issue {issue} is assigned to Copilot.", Severity.Success);
        }
        else
        {
            snackbar.Add($"Issue {issue} exists, but assigning Copilot failed: {result.AssignmentError}", Severity.Warning,
                o => o.RequireInteraction = true);
        }
    }
}
