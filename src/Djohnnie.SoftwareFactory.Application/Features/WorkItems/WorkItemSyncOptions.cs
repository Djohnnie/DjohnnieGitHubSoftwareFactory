namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class WorkItemSyncOptions
{
    /// <summary>
    /// How long to wait before each retry of a work item that failed with a transient error (a GitHub server error, a
    /// network error, a timeout or a database error); one retry per delay. The other errors aren't retried.
    /// </summary>
    public TimeSpan[] RetryDelays { get; set; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];
}
