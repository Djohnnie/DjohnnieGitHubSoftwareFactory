using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// URLs and icons per work item type. The work item and template pages each serve all types and read the type
/// from the first path segment, e.g. <c>/stories/12</c> or <c>/bug-templates/new</c>.
/// </summary>
public static class WorkItemRoutes
{
    public static string ListPath(WorkItemType type)
    {
        return "/" + Segment(type);
    }

    public static string NewPath(WorkItemType type, int? parentId = null, int? templateId = null)
    {
        var query = new List<string>();
        if (parentId is { } parent)
        {
            query.Add($"parent={parent}");
        }
        if (templateId is { } template)
        {
            query.Add($"template={template}");
        }
        return $"{ListPath(type)}/new{(query.Count == 0 ? string.Empty : "?" + string.Join('&', query))}";
    }

    public static string ItemPath(WorkItemType type, int id)
    {
        return $"{ListPath(type)}/{id}";
    }

    public static string TemplatesPath(WorkItemType type)
    {
        return $"/{TemplateSegment(type)}";
    }

    public static string NewTemplatePath(WorkItemType type)
    {
        return $"{TemplatesPath(type)}/new";
    }

    public static string EditTemplatePath(WorkItemType type, int id)
    {
        return $"{TemplatesPath(type)}/edit/{id}";
    }

    /// <summary>The type of a work item page (<c>/epics...</c>) or template page (<c>/epic-templates...</c>), if it is one.</summary>
    public static WorkItemType? TypeOf(string uri)
    {
        var path = Uri.TryCreate(uri, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : uri.Split('?', '#')[0];
        var first = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        foreach (var type in WorkItemTypeRules.All)
        {
            if (first.Equals(Segment(type), StringComparison.OrdinalIgnoreCase)
                || first.Equals(TemplateSegment(type), StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }
        return null;
    }

    public static WorkItemType TypeOf(NavigationManager navigation)
    {
        return TypeOf(navigation.Uri) ?? WorkItemType.Epic;
    }

    /// <summary>Whether the URI is a page of one existing work item, which belongs to the repository it was opened in.</summary>
    public static bool IsItemPage(string uri)
    {
        var path = Uri.TryCreate(uri, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : uri.Split('?', '#')[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 && int.TryParse(segments[1], out _)
            && WorkItemTypeRules.All.Any(type => segments[0].Equals(Segment(type), StringComparison.OrdinalIgnoreCase));
    }

    public static string Icon(WorkItemType type)
    {
        return type switch
        {
            WorkItemType.Epic => Icons.Material.Filled.Flag,
            WorkItemType.Feature => Icons.Material.Filled.Extension,
            WorkItemType.Story => Icons.Material.Filled.AutoStories,
            WorkItemType.Bug => Icons.Material.Filled.BugReport,
            _ => Icons.Material.Filled.Checklist
        };
    }

    private static string Segment(WorkItemType type)
    {
        return type switch
        {
            WorkItemType.Story => "stories",
            _ => type.Label + "s"
        };
    }

    private static string TemplateSegment(WorkItemType type)
    {
        return $"{type.Label}-templates";
    }
}
