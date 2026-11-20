using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Pages;

/// <summary>What a load of <see cref="WorkItemAgentTasks"/> changed, so the page can keep its open tab and follow an ended task.</summary>
/// <param name="HadTasks">The item had tasks (so the agent runs tab) before the load.</param>
/// <param name="HasTasks">It has tasks after it.</param>
/// <param name="Ended">A task that was active before the load no longer is.</param>
public readonly record struct WorkItemAgentTasksChange(bool HadTasks, bool HasTasks, bool Ended);

/// <summary>
/// The DGHSF agent worker's tasks on a work item, newest first, each with its runs. While a task is active they reload by
/// themselves every <see cref="RefreshInterval"/>; each reload schedules the next.
/// </summary>
/// <param name="onUi">
/// Runs a background reload on the component's renderer and lets the page react to it (<c>InvokeAsync</c>); the page
/// renders again afterwards.
/// </param>
/// <param name="reloaded">Called on the renderer after every background reload.</param>
public sealed class WorkItemAgentTasks(AgentRunService agentRuns, ISnackbar snackbar, Func<Func<Task>, Task> onUi,
    Func<WorkItemAgentTasksChange, Task> reloaded) : IDisposable
{
    /// <summary>How often the tasks reload while one of them is active.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private Timer? _timer;
    private string? _repositoryFullName;
    private Guid? _workItemId;

    public IReadOnlyList<AgentRunOverview> Tasks { get; private set; } = [];

    public bool HasTasks
    {
        get
        {
            return Tasks.Count > 0;
        }
    }

    /// <summary>The runs of the item's tasks, counting the run a task waits to start.</summary>
    public int RunCount
    {
        get
        {
            return Tasks.Sum(t => t.Run.Attempts + (t.Run.Status is AgentRunStatus.Pending or AgentRunStatus.Queued ? 1 : 0));
        }
    }

    /// <summary>
    /// Reads the tasks of the work item, or forgets them when <paramref name="workItemId"/> is <c>null</c> (the item doesn't run
    /// on the worker, has no issue or its repository is gone). A failure is shown and keeps the tasks read before.
    /// </summary>
    public async Task<WorkItemAgentTasksChange> LoadAsync(string repositoryFullName, Guid? workItemId)
    {
        _repositoryFullName = repositoryFullName;
        _workItemId = workItemId;
        var hadTasks = HasTasks;
        var wasActive = Tasks.Any(t => t.Run.IsActive);
        if (workItemId is not { } id)
        {
            Tasks = [];
        }
        else
        {
            try
            {
                Tasks = await agentRuns.GetWorkItemTasksAsync(repositoryFullName, id);
            }
            catch (Exception ex) when (WorkItemActions.IsExpected(ex))
            {
                snackbar.Add($"Could not load the agent worker's runs: {ex.Message}", Severity.Error);
            }
        }

        ScheduleRefresh();
        return new(hadTasks, HasTasks, wasActive && !Tasks.Any(t => t.Run.IsActive));
    }

    /// <summary>Forgets the tasks of the previous item and stops reloading them.</summary>
    public void Clear()
    {
        Stop();
        Tasks = [];
        _workItemId = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private void ScheduleRefresh()
    {
        Stop();
        if (_repositoryFullName is not { } repository || !Tasks.Any(t => t.Run.IsActive))
        {
            return;
        }
        var workItemId = _workItemId;
        _timer = new Timer(_ =>
        {
            _ = onUi(async () =>
            {
                if (workItemId != _workItemId)
                {
                    // The page moved on to another item in the meantime.
                    return;
                }
                await reloaded(await LoadAsync(repository, workItemId));
            });
        }, null, RefreshInterval, Timeout.InfiniteTimeSpan);
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
