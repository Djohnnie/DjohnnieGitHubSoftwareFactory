using System.Text;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// The whole backlog of a repository as a compact outline for the background agents: every item with its type, status,
/// title and id, children indented under their parent. Descriptions are left out; <see cref="WorkItemTools"/> reads them.
/// </summary>
public static class WorkItemBacklog
{
    public const int DefaultMaxLength = 30_000;

    public static string Outline(IReadOnlyList<WorkItem> items, int maxLength = DefaultMaxLength)
    {
        if (items.Count == 0)
        {
            return "(The backlog is empty.)";
        }

        var ids = items.Select(item => item.Id).ToHashSet();
        var children = items.Where(item => item.ParentId is { } parent && ids.Contains(parent)).ToLookup(item => item.ParentId!.Value);
        var lines = new List<string>();
        void Write(WorkItem item, int depth)
        {
            lines.Add($"{new string(' ', depth * 2)}- {Line(item)}");
            foreach (var child in Ordered(children[item.Id]))
            {
                Write(child, depth + 1);
            }
        }

        foreach (var root in Ordered(items.Where(item => item.ParentId is not { } parent || !ids.Contains(parent))))
        {
            Write(root, 0);
        }

        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (builder.Length + lines[i].Length + 1 > maxLength)
            {
                builder.Append($"[{lines.Count - i} more items are not shown: the backlog is too long.]");
                break;
            }
            builder.AppendLine(lines[i]);
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>E.g. <c>User story [accepted, #12] Return problem details (id: 3f2a…)</c>.</summary>
    public static string Line(WorkItem item)
    {
        var status = Status(item.Status) + (item.IssueNumber is { } number ? $", #{number}" : string.Empty);
        return $"{item.Type.DisplayName} [{status}] {item.Title.ReplaceLineEndings(" ")} (id: {item.Id})";
    }

    public static string Status(WorkItemStatus status)
    {
        return status switch
        {
            WorkItemStatus.Draft => "draft",
            WorkItemStatus.Generated => "generated draft",
            WorkItemStatus.Accepted => "accepted",
            WorkItemStatus.InProgress => "in progress",
            _ => "done"
        };
    }

    /// <summary>Epics, features and stories first (in type order), then by title.</summary>
    private static IEnumerable<WorkItem> Ordered(IEnumerable<WorkItem> items)
    {
        return items.OrderBy(item => item.Type).ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase);
    }
}
