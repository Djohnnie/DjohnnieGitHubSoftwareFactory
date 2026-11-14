namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>URLs of the mindmap pages: the overview of all maps, a new map and one stored map.</summary>
public static class MindmapRoutes
{
    public const string ListPath = "/mindmaps";

    public const string NewPath = "/mindmaps/new";

    /// <param name="ideaId">An idea to select and bring into view.</param>
    public static string MapPath(Guid id, Guid? ideaId = null)
    {
        return ideaId is { } idea ? $"/mindmaps/{id}?idea={idea}" : $"/mindmaps/{id}";
    }
}
