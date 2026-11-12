using System.Globalization;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>How a GitHub Actions run is shown: its state as a label, color and icon, and its duration.</summary>
public static class WorkflowRunDisplay
{
    /// <summary>The conclusion of a completed run, otherwise its status: "Success", "Failure", "In progress", "Queued", …</summary>
    public static string Label(GitHubWorkflowRun run)
    {
        var state = run.IsCompleted ? run.Conclusion ?? "completed" : run.Status ?? "unknown";
        var words = state.Replace('_', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    public static Color Color(GitHubWorkflowRun run)
    {
        if (!run.IsCompleted)
        {
            return MudBlazor.Color.Info;
        }

        return run.Conclusion switch
        {
            "success" => MudBlazor.Color.Success,
            "failure" or "timed_out" or "startup_failure" => MudBlazor.Color.Error,
            "action_required" => MudBlazor.Color.Warning,
            _ => MudBlazor.Color.Default
        };
    }

    public static string Icon(GitHubWorkflowRun run)
    {
        if (!run.IsCompleted)
        {
            return run.Status == "in_progress" ? Icons.Material.Filled.Autorenew : Icons.Material.Filled.Schedule;
        }

        return run.Conclusion switch
        {
            "success" => Icons.Material.Filled.CheckCircle,
            "failure" or "timed_out" or "startup_failure" => Icons.Material.Filled.Cancel,
            "action_required" => Icons.Material.Filled.Warning,
            "skipped" => Icons.Material.Filled.SkipNext,
            _ => Icons.Material.Filled.DoNotDisturbOn
        };
    }

    /// <summary>"1h 2m", "3m 25s" or "45s"; empty while the run hasn't completed.</summary>
    public static string Duration(GitHubWorkflowRun run)
    {
        if (run.Duration is not { } value)
        {
            return string.Empty;
        }
        if (value.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}h {value.Minutes}m");
        }
        if (value.TotalMinutes >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{value.Minutes}m {value.Seconds}s");
        }
        return string.Create(CultureInfo.InvariantCulture, $"{value.Seconds}s");
    }

    /// <summary>When the run (its latest attempt) started.</summary>
    public static DateTimeOffset StartedAt(GitHubWorkflowRun run)
    {
        return run.RunStartedAt ?? run.CreatedAt;
    }
}
