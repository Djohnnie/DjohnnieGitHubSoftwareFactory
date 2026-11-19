namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>URLs of the mindmap pages: the overview of all maps, a new map and one stored map.</summary>
public static class MindmapRoutes
{
    public const string ListPath = "/mindmaps";

    public const string NewPath = "/mindmaps/new";

    /// <summary>The page where the Mindmap Planner proposes the work items of a stored map.</summary>
    public static string GeneratePath(Guid id)
    {
        return $"{Contracts.AppPaths.Mindmap(id)}/generate";
    }

    /// <param name="ideaId">An idea to select and bring into view.</param>
    public static string MapPath(Guid id, Guid? ideaId = null)
    {
        var path = Contracts.AppPaths.Mindmap(id);
        return ideaId is { } idea ? $"{path}?idea={idea}" : path;
    }
}
