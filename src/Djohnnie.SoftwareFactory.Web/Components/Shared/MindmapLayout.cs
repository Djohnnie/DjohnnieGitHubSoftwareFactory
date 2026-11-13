using System.Globalization;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// The editing operations of the mindmap page: where a new idea goes, moving an idea with everything below it,
/// moving it below another idea and removing it. Positions are pixels relative to the central idea, which stays at 0,0.
/// </summary>
public static class MindmapLayout
{
    /// <summary>The room an idea needs around its centre, horizontally (matches the node width in <c>Mindmaps.razor.css</c>).</summary>
    public const double NodeWidth = 230;

    /// <summary>The room an idea needs around its centre, vertically.</summary>
    public const double NodeHeight = 62;

    /// <summary>The number of branch colours in <c>Mindmaps.razor.css</c>.</summary>
    public const int BranchColors = 6;

    private const double RootRadiusX = 300;
    private const double RootRadiusY = 175;
    private const double ChildDistance = 250;

    // The ideas around the centre fill the right and left first, then the diagonals, then the rest.
    private static readonly double[] RootAngles =
        new double[] { 0, 180, 315, 225, 45, 135, 270, 90 }
            .Concat(Enumerable.Range(0, 24).Select(step => step * 15.0))
            .Distinct()
            .ToArray();

    // Ideas further out go on in the direction of their parent, fanning out to both sides.
    private static readonly double[] ChildOffsets = [0, 28, -28, 56, -56, 84, -84, 112, -112];

    private static readonly double[] Rings = [1, 1.5, 2, 2.6];

    /// <summary>Adds a new idea below <paramref name="parentId"/>, at a free place next to it.</summary>
    public static MindmapNode AddChild(Mindmap map, Guid parentId, string text)
    {
        var (x, y) = PlaceChild(map, parentId);
        var node = new MindmapNode { Id = Guid.NewGuid(), ParentId = parentId, Text = text, X = x, Y = y };
        map.Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// A free place for a new idea below <paramref name="parentId"/>: around the centre for the central idea, otherwise
    /// further out in the direction of the parent. The first place where the idea doesn't overlap another one wins.
    /// </summary>
    /// <param name="ignore">Ideas that don't count as being in the way (the ones being moved).</param>
    public static (double X, double Y) PlaceChild(Mindmap map, Guid parentId, IReadOnlySet<Guid>? ignore = null)
    {
        var parent = map.Find(parentId) ?? throw new ArgumentException("The parent idea doesn't exist.", nameof(parentId));
        var others = map.Nodes.Where(node => ignore is null || !ignore.Contains(node.Id)).ToList();
        var candidates = new List<(double X, double Y)>();
        foreach (var ring in Rings)
        {
            if (parent.ParentId is not { } grandparentId)
            {
                candidates.AddRange(RootAngles.Select(angle => Around(parent, angle, RootRadiusX * ring, RootRadiusY * ring)));
                continue;
            }

            var grandparent = map.Find(grandparentId)!;
            var dx = parent.X - grandparent.X;
            var dy = parent.Y - grandparent.Y;
            var direction = dx == 0 && dy == 0 ? 0 : Math.Atan2(dy, dx) * 180 / Math.PI;
            candidates.AddRange(ChildOffsets.Select(offset => Around(parent, direction + offset, ChildDistance * ring, ChildDistance * ring)));
        }

        var best = candidates[0];
        var bestClearance = double.MinValue;
        foreach (var candidate in candidates)
        {
            var clearance = Clearance(candidate, others);
            if (clearance >= 1)
            {
                return candidate;
            }
            if (clearance > bestClearance)
            {
                best = candidate;
                bestClearance = clearance;
            }
        }
        return best;
    }

    /// <summary>Moves an idea and everything below it; moving the central idea moves the whole map.</summary>
    public static void Move(Mindmap map, Guid id, double dx, double dy)
    {
        foreach (var node in map.SubtreeOf(id))
        {
            node.X = Math.Round(node.X + dx, 1);
            node.Y = Math.Round(node.Y + dy, 1);
        }
    }

    /// <summary>Whether an idea can move below another one: not the central idea, and not below itself or an idea below it.</summary>
    public static bool CanReparent(Mindmap map, Guid id, Guid newParentId)
    {
        return map.Find(id) is { ParentId: not null } node
            && map.Find(newParentId) is not null
            && node.ParentId != newParentId
            && map.SubtreeOf(id).All(n => n.Id != newParentId);
    }

    /// <summary>
    /// Moves an idea with everything below it to another parent. The idea goes to a free place next to that parent and
    /// the ideas below it are placed again, level by level, because the branch may now point in another direction.
    /// </summary>
    public static bool Reparent(Mindmap map, Guid id, Guid newParentId)
    {
        if (!CanReparent(map, id, newParentId))
        {
            return false;
        }

        var subtree = map.SubtreeOf(id);
        var unplaced = subtree.Select(n => n.Id).ToHashSet();
        map.Find(id)!.ParentId = newParentId;
        foreach (var node in subtree)
        {
            (node.X, node.Y) = PlaceChild(map, node.ParentId!.Value, unplaced);
            unplaced.Remove(node.Id);
        }
        return true;
    }

    /// <summary>Removes an idea with everything below it; the central idea can't be removed.</summary>
    /// <returns>The number of ideas removed.</returns>
    public static int Remove(Mindmap map, Guid id)
    {
        if (map.Find(id) is not { ParentId: not null })
        {
            return 0;
        }

        var subtree = map.SubtreeOf(id).Select(n => n.Id).ToHashSet();
        return map.Nodes.RemoveAll(node => subtree.Contains(node.Id));
    }

    /// <summary>
    /// The parent a new work item of <paramref name="type"/> for an idea gets: the work item of the nearest idea above it that
    /// is linked to an item of a type that can contain it (e.g. the epic above a feature), or <c>null</c> when there is none.
    /// </summary>
    public static Guid? ProposedParent(Mindmap map, Guid ideaId, WorkItemType type, IReadOnlyDictionary<Guid, WorkItem> workItems)
    {
        var node = map.Find(ideaId);
        var guard = 0;
        while (node?.ParentId is { } parentId && guard++ <= map.Nodes.Count)
        {
            node = map.Find(parentId);
            if (node?.WorkItemId is { } linked && workItems.TryGetValue(linked, out var item) && type.ParentTypes.Contains(item.Type))
            {
                return item.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// The branch an idea belongs to (which idea around the centre it is below), for its colour; -1 for the central idea.
    /// </summary>
    public static int BranchOf(Mindmap map, Guid id)
    {
        var node = map.Find(id);
        if (node?.ParentId is null)
        {
            return -1;
        }

        var guard = 0;
        while (map.Find(node.ParentId!.Value) is { ParentId: not null } parent && guard++ <= map.Nodes.Count)
        {
            node = parent;
        }

        var branches = map.ChildrenOf(map.Root!.Id).ToList();
        var index = branches.FindIndex(branch => branch.Id == node.Id);
        return index < 0 ? 0 : index % BranchColors;
    }

    /// <summary>
    /// The SVG path of the line from a parent to a child: a curve that leaves and arrives horizontally (or vertically,
    /// when the child is mostly above or below). <c>Mindmaps.razor.js</c> draws the same curve while dragging.
    /// </summary>
    public static string EdgePath(MindmapNode from, MindmapNode to)
    {
        var (x1, y1, x2, y2) = (from.X, from.Y, to.X, to.Y);
        if (Math.Abs(x2 - x1) >= Math.Abs(y2 - y1))
        {
            var mx = (x1 + x2) / 2;
            return string.Create(CultureInfo.InvariantCulture, $"M{x1:0.#} {y1:0.#} C{mx:0.#} {y1:0.#} {mx:0.#} {y2:0.#} {x2:0.#} {y2:0.#}");
        }

        var my = (y1 + y2) / 2;
        return string.Create(CultureInfo.InvariantCulture, $"M{x1:0.#} {y1:0.#} C{x1:0.#} {my:0.#} {x2:0.#} {my:0.#} {x2:0.#} {y2:0.#}");
    }

    private static (double X, double Y) Around(MindmapNode center, double degrees, double radiusX, double radiusY)
    {
        var radians = degrees * Math.PI / 180;
        return (Math.Round(center.X + radiusX * Math.Cos(radians), 1), Math.Round(center.Y + radiusY * Math.Sin(radians), 1));
    }

    // How far a place is from the nearest idea, in node sizes: 1 or more means it doesn't overlap any idea.
    private static double Clearance((double X, double Y) place, List<MindmapNode> others)
    {
        var clearance = double.MaxValue;
        foreach (var node in others)
        {
            var distance = Math.Max(Math.Abs(place.X - node.X) / NodeWidth, Math.Abs(place.Y - node.Y) / NodeHeight);
            clearance = Math.Min(clearance, distance);
        }
        return clearance;
    }
}
