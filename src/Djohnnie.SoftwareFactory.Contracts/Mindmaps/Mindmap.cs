namespace Djohnnie.SoftwareFactory.Contracts.Mindmaps;

/// <summary>
/// A mind map of one repository: a single central idea (the root, the only node without a parent) with ideas below it.
/// It works as a simple to-do list: every idea can be marked done.
/// </summary>
public sealed record Mindmap
{
    public const int MaxTextLength = 200;
    public const int MaxNodes = 500;
    public const string DefaultIdea = "Central idea";

    public Guid Id { get; set; }

    public string RepositoryFullName { get; set; } = string.Empty;

    /// <summary>All ideas, the central one included, in no particular order.</summary>
    public List<MindmapNode> Nodes { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The central idea, or <c>null</c> when the map has none (only an invalid map).</summary>
    public MindmapNode? Root
    {
        get
        {
            return Nodes.FirstOrDefault(node => node.ParentId is null);
        }
    }

    /// <summary>The map's title: its central idea.</summary>
    public string Title
    {
        get
        {
            return Root?.Text ?? string.Empty;
        }
    }

    /// <summary>A new, unsaved map with only a central idea in the middle.</summary>
    public static Mindmap Create(string repositoryFullName, string idea = DefaultIdea)
    {
        return new Mindmap
        {
            RepositoryFullName = repositoryFullName,
            Nodes = [new MindmapNode { Id = Guid.NewGuid(), Text = idea }]
        };
    }

    public MindmapNode? Find(Guid id)
    {
        return Nodes.FirstOrDefault(node => node.Id == id);
    }

    public IEnumerable<MindmapNode> ChildrenOf(Guid id)
    {
        return Nodes.Where(node => node.ParentId == id);
    }

    /// <summary>The node and everything below it, parents before their children.</summary>
    public List<MindmapNode> SubtreeOf(Guid id)
    {
        var result = new List<MindmapNode>();
        if (Find(id) is not { } start)
        {
            return result;
        }

        var seen = new HashSet<Guid> { start.Id };
        result.Add(start);
        for (var i = 0; i < result.Count; i++)
        {
            foreach (var child in ChildrenOf(result[i].Id))
            {
                if (seen.Add(child.Id))
                {
                    result.Add(child);
                }
            }
        }
        return result;
    }

    /// <summary>How deep a node is: 0 for the central idea, 1 for the ideas around it, ...</summary>
    public int DepthOf(Guid id)
    {
        var depth = 0;
        var node = Find(id);
        while (node?.ParentId is { } parentId && depth <= Nodes.Count)
        {
            node = Find(parentId);
            depth++;
        }
        return depth;
    }

    /// <summary>
    /// What is wrong with the map's structure, or <c>null</c> when it is a valid tree: exactly one central idea,
    /// unique ids, and every other idea below an existing idea, without cycles.
    /// </summary>
    public string? StructureProblem()
    {
        var roots = Nodes.Count(node => node.ParentId is null);
        if (roots != 1)
        {
            return roots == 0 ? "A mindmap needs a central idea." : "A mindmap has only one central idea.";
        }
        if (Nodes.Select(node => node.Id).Distinct().Count() != Nodes.Count || Nodes.Any(node => node.Id == Guid.Empty))
        {
            return "Every idea needs its own id.";
        }
        if (Nodes.Any(node => node.ParentId is { } parentId && Find(parentId) is null))
        {
            return "An idea refers to an idea that doesn't exist.";
        }
        if (SubtreeOf(Root!.Id).Count != Nodes.Count)
        {
            return "Every idea must be connected to the central idea.";
        }
        return null;
    }

    /// <summary>A copy whose nodes can be edited without changing this map.</summary>
    public Mindmap Copy()
    {
        return this with { Nodes = Nodes.Select(node => node with { }).ToList() };
    }
}

/// <summary>One idea of a <see cref="Mindmap"/>.</summary>
public sealed record MindmapNode
{
    public Guid Id { get; set; }

    /// <summary>The idea this one belongs to, or <c>null</c> for the central idea.</summary>
    public Guid? ParentId { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>The horizontal position of the idea's centre in pixels, relative to the central idea.</summary>
    public double X { get; set; }

    /// <summary>The vertical position of the idea's centre in pixels, relative to the central idea.</summary>
    public double Y { get; set; }

    public bool IsDone { get; set; }

    /// <summary>The epic, feature, user story, bug or task the idea is linked to, if any; closing it marks the idea done.</summary>
    public Guid? WorkItemId { get; set; }
}

/// <summary>A mindmap in the list of a repository's mindmaps.</summary>
public sealed record MindmapSummary(Guid Id, string Title, int IdeaCount, int DoneCount, DateTimeOffset UpdatedAt);

public sealed record SaveMindmapRequest(Mindmap Mindmap);

/// <summary>Links an idea of a stored mindmap to a work item of the same repository, or unlinks it (<c>null</c>).</summary>
public sealed record LinkMindmapIdeaRequest(string RepositoryFullName, Guid MindmapId, Guid IdeaId, Guid? WorkItemId);

/// <summary>
/// Asks the Mindmap Work Item Writer for the title and description of a new work item for an idea of a stored mindmap.
/// </summary>
/// <param name="TemplateId">The template to follow; <c>null</c> for none.</param>
/// <param name="ParentId">The parent the new item will get, if chosen already.</param>
public sealed record GenerateMindmapWorkItemRequest(string RepositoryFullName, Guid MindmapId, Guid IdeaId, WorkItems.WorkItemType Type, Guid? TemplateId, Guid? ParentId);

/// <summary>An idea of a mindmap that is linked to a work item.</summary>
public sealed record MindmapIdeaLink(Guid MindmapId, string MindmapTitle, Guid IdeaId, string IdeaText, bool IsDone);
