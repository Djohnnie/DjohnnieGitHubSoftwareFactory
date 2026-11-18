using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Contracts;

/// <summary>
/// The relative addresses of DGHSF's pages that the agents hand out, so a chat reply can link to them. The web app's
/// route helpers build the same addresses.
/// </summary>
public static class AppPaths
{
    public const string MindmapsSegment = "mindmaps";

    public static string WorkItem(WorkItemType type, Guid id)
    {
        return $"/{type.PathSegment}/{id}";
    }

    public static string Mindmap(Guid id)
    {
        return $"/{MindmapsSegment}/{id}";
    }

    /// <summary>
    /// Whether <paramref name="url"/> is exactly the page of one work item or one mindmap, without a query or fragment.
    /// Only those pages may be linked from text that can't be trusted, such as model output.
    /// </summary>
    public static bool IsLinkablePage(string? url)
    {
        if (url is null || !url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal) || url.IndexOfAny(['\\', '?', '#', '%']) >= 0)
        {
            return false;
        }

        var segments = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || !Guid.TryParse(segments[1], out var id))
        {
            return false;
        }

        return segments[0].Equals(MindmapsSegment, StringComparison.OrdinalIgnoreCase)
            ? url.Equals(Mindmap(id), StringComparison.OrdinalIgnoreCase)
            : WorkItemTypeRules.All.Any(type => url.Equals(WorkItem(type, id), StringComparison.OrdinalIgnoreCase));
    }
}
