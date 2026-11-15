using System.ComponentModel;
using System.Text;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Lets a background agent read the full description of a work item from the backlog outline
/// (<see cref="WorkItemBacklog"/>). Only items of the analyzed repository can be read, at most
/// <paramref name="maxReads"/> per agent run.
/// </summary>
/// <param name="getWorkItem">Gets a work item of the analyzed repository by id, or <c>null</c> when it doesn't exist there.</param>
/// <param name="maxReads">How many work items the agent may read.</param>
public sealed class WorkItemTools(Func<Guid, CancellationToken, Task<WorkItem?>> getWorkItem, int maxReads = WorkItemTools.DefaultMaxReads)
{
    public const int DefaultMaxReads = 20;

    /// <summary>Longer descriptions are cut off, so a single item doesn't flood the conversation.</summary>
    public const int MaxDescriptionLength = 16_000;

    private int _reads;

    /// <summary>How many work items the agent read.</summary>
    public int Reads
    {
        get
        {
            return Volatile.Read(ref _reads);
        }
    }

    public IList<AITool> AsTools()
    {
        return [AIFunctionFactory.Create(GetWorkItemAsync, "get_work_item")];
    }

    [Description("Gets one work item of the backlog by its id: type, title, status, parent and full description. Only a limited number of items can be read: choose the ones that matter.")]
    public async Task<string> GetWorkItemAsync(
        [Description("The id of the work item, as shown in the backlog outline.")] string id,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse((id ?? string.Empty).Trim(), out var workItemId))
        {
            return $"'{id}' is not a work item id; use an id from the backlog outline.";
        }
        if (Interlocked.Increment(ref _reads) > maxReads)
        {
            return $"The limit of {maxReads} work items is reached; continue with what you have read.";
        }

        var item = await getWorkItem(workItemId, cancellationToken);
        if (item is null)
        {
            return $"There is no work item with id {workItemId} in this repository.";
        }

        var builder = new StringBuilder()
            .AppendLine(WorkItemBacklog.Line(item));
        if (item.Parent is { } parent)
        {
            builder.AppendLine($"Part of: {parent.Type.DisplayName} {parent.Title} (id: {parent.Id})");
        }
        builder.AppendLine();
        var description = item.Description.Trim();
        builder.Append(description.Length == 0
            ? "(No description.)"
            : description.Length <= MaxDescriptionLength
                ? description
                : $"{description[..MaxDescriptionLength]}\n\n[Cut off: the description has {description.Length} characters; only the first {MaxDescriptionLength} are shown.]");
        return builder.ToString();
    }
}
