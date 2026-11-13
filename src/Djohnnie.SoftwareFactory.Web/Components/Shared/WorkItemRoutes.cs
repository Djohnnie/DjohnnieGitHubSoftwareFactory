using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// URLs and icons per work item type. The work item and template pages each serve all types and read the type
/// from the first path segment, e.g. <c>/stories/{id}</c> or <c>/bug-templates/new</c>.
/// </summary>
public static class WorkItemRoutes
{
    public static string ListPath(WorkItemType type)
    {
        return "/" + Segment(type);
    }

    /// <param name="mindmapId">With <paramref name="ideaId"/>: the mindmap idea the new item is written for and linked to on save.</param>
    public static string NewPath(WorkItemType type, Guid? parentId = null, Guid? templateId = null, Guid? mindmapId = null, Guid? ideaId = null)
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
        if (mindmapId is { } mindmap && ideaId is { } idea)
        {
            query.Add($"mindmap={mindmap}");
            query.Add($"idea={idea}");
        }
        return $"{ListPath(type)}/new{(query.Count == 0 ? string.Empty : "?" + string.Join('&', query))}";
    }

    public static string ItemPath(WorkItemType type, Guid id)
    {
        return $"{ListPath(type)}/{id}";
    }

    /// <summary>The page that generates and reviews the child items of a stored item.</summary>
    public static string GeneratePath(WorkItemType type, Guid id)
    {
        return $"{ItemPath(type, id)}/generate";
    }

    public static string TemplatesPath(WorkItemType type)
    {
        return $"/{TemplateSegment(type)}";
    }

    public static string NewTemplatePath(WorkItemType type)
    {
        return $"{TemplatesPath(type)}/new";
    }

    public static string EditTemplatePath(WorkItemType type, Guid id)
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

    /// <summary>
    /// Whether a page's parameters are out of step with the route. On navigation the cascaded route data reaches the
    /// current page before its own parameters are updated, or before it is removed: the URL is already the new one while
    /// <paramref name="id"/> is still the old one. Such a combination means nothing and must not be loaded or acted on.
    /// </summary>
    public static bool IsOutOfStep(Microsoft.AspNetCore.Components.RouteData? routeData, Type page, Guid? id)
    {
        return routeData is not null && (routeData.PageType != page || !Equals(routeData.RouteValues.GetValueOrDefault("Id"), id));
    }

    /// <summary>Whether the URI is a page of one existing work item, which belongs to the repository it was opened in.</summary>
    public static bool IsItemPage(string uri)
    {
        var path = Uri.TryCreate(uri, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : uri.Split('?', '#')[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var isItemOrGeneratePath = segments.Length == 2 || (segments.Length == 3 && segments[2].Equals("generate", StringComparison.OrdinalIgnoreCase));
        return isItemOrGeneratePath && Guid.TryParse(segments[1], out _)
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
