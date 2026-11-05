using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Pages;

/// <summary>Confirmations and feedback shared by the epics list and the epic editor.</summary>
internal static class EpicActions
{
    public static bool IsExpected(Exception ex)
    {
        return ex is InvalidOperationException or GitHubApiException or HttpRequestException;
    }

    public static Task<bool?> ConfirmAcceptAsync(IDialogService dialogs, Epic epic, string repositoryFullName)
    {
        var agent = string.IsNullOrEmpty(epic.CustomAgent) ? "the default Copilot agent" : $"the custom agent '{epic.CustomAgent}'";
        var model = string.IsNullOrEmpty(epic.Model) ? "the default model" : $"model '{epic.Model}'";
        return dialogs.ShowMessageBoxAsync("Accept epic",
            $"This creates a GitHub issue '{epic.Title}' labelled '{Epic.Label}' in {repositoryFullName} and assigns it to the " +
            $"Copilot cloud agent using {agent} and {model}. Afterwards the epic content can no longer be edited here.",
            yesText: "Accept & assign", cancelText: "Cancel");
    }

    public static Task<bool?> ConfirmDeleteAsync(IDialogService dialogs, Epic epic)
    {
        return dialogs.ShowMessageBoxAsync("Delete epic",
            epic.IssueNumber is { } number
                ? $"Delete '{epic.Title}' from DGHSF? GitHub issue #{number} is not touched; close it on GitHub if needed."
                : $"Delete the draft '{epic.Title}'? This cannot be undone.",
            yesText: "Delete", cancelText: "Cancel");
    }

    public static void Report(ISnackbar snackbar, EpicAcceptResult result)
    {
        var issue = $"#{result.Epic.IssueNumber}";
        if (result.AssignmentError is null)
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
