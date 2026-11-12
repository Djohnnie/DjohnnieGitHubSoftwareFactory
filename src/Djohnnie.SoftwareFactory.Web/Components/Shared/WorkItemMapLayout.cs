using System.Globalization;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>Where a node sits on the work item map.</summary>
public enum WorkItemMapRole
{
    /// <summary>The expanded epic in the middle of the map.</summary>
    Center,

    /// <summary>A descendant of the expanded epic, on the ring of its <see cref="WorkItemMapNode.Depth"/>.</summary>
    Child,

    /// <summary>An epic around the repository hub, when no epic is expanded.</summary>
    Epic,

    /// <summary>A bug or task that isn't part of another item, on the outer ring around the repository hub.</summary>
    Loose
}

/// <summary>A work item on the map, positioned in percent of the map's width (<see cref="X"/>) and height (<see cref="Y"/>).</summary>
/// <param name="FromX">Where the node grows from when it appears: its parent's position.</param>
/// <param name="Scale">The size of the node relative to its normal size: below 1 when its ring is too full to fit otherwise.</param>
/// <param name="Crowded">The node doesn't fit with its label even at <see cref="WorkItemMapLayout.MinScale"/>, so the label is only shown on hover or focus.</param>
public sealed record WorkItemMapNode(
    WorkItem Item,
    WorkItemMapRole Role,
    int Depth,
    double X,
    double Y,
    double FromX,
    double FromY,
    int DescendantCount,
    double Scale,
    bool Crowded);

/// <summary>The items below an epic by status; <see cref="Accepted"/> includes the ones in progress.</summary>
public sealed record WorkItemMapProgress(int Draft, int Accepted, int Done)
{
    public int Total => Draft + Accepted + Done;

    /// <summary>The share that is done (closed), rounded down so 100 % means everything is closed; 0 without items.</summary>
    public int PercentDone => Total == 0 ? 0 : Done * 100 / Total;

    /// <summary>The width of each part of the bar, in percent of the bar.</summary>
    public double Share(int count)
    {
        return Total == 0 ? 0 : count * 100d / Total;
    }
}

/// <summary>A curved line from a parent to a child, as an SVG path in the map's 0-100 coordinate space.</summary>
/// <param name="Depth">The depth of the child, to stagger the drawing.</param>
public sealed record WorkItemMapEdge(string Key, string Path, WorkItemType Type, int Depth);

/// <summary>
/// The radial layout of the work item map: either one expanded epic in the middle with its descendants on rings around it
/// (each subtree gets an angle proportional to its number of leaves), or, without an expanded epic, the repository hub
/// with all epics around it and the standalone bugs and tasks on an outer ring.
/// The rings spread over all the room of the map, which the page measures in the browser. Each ring is then fitted to that size in
/// pixels, from the inside out: every node finds its own radius (only the nodes that would overlap their parent, the epic in the
/// middle or the edge move out), a full ring staggers every other node onto a second radius, the ring's nodes get smaller, and only
/// the nodes that don't fit even then show their label on hover only. Equal steps of the tree get equal room on the ellipse (see Warp).
/// </summary>
public sealed class WorkItemMapLayout
{
    /// <summary>The map size used until the browser has reported the real one.</summary>
    public const double DefaultWidth = 1150;

    public const double DefaultHeight = 560;

    /// <summary>Nodes don't get smaller than this; a ring that needs more room hides its labels instead.</summary>
    public const double MinScale = 0.55;

    private const double CenterX = 50;
    private const double CenterY = 50;

    // The rings are ellipses, so the map uses the wide stage; fitting keeps the nodes and their labels inside it.
    private const double RadiusX = 44;
    private const double RadiusY = 41;

    // How far every other node of a full ring moves away from the ring.
    private const double Stagger = 0.16;

    // The room an inner ring leaves for each ring outside it.
    private const double RingGap = 0.1;

    // A child is at least this much further out than its parent; the overlap checks keep their boxes apart.
    private const double ParentGap = 0.07;

    private const int WarpSteps = 720;

    private readonly ILookup<Guid?, WorkItem> _children;
    private readonly Dictionary<Guid, int> _weights = [];
    private readonly List<Placement> _placements = [];
    private readonly Dictionary<(double, double), double[]> _warps = [];
    private readonly double _width;
    private readonly double _height;

    private WorkItemMapLayout(IReadOnlyList<WorkItem> items, double width, double height)
    {
        var ids = items.Select(i => i.Id).ToHashSet();
        _children = items.Where(i => i.ParentId is { } parent && ids.Contains(parent)).ToLookup(i => i.ParentId);
        _width = Math.Max(width, 200);
        _height = Math.Max(height, 200);
        Epics = [.. items.Where(i => i.Type == WorkItemType.Epic).OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.CreatedAt)];
        Loose = [.. items.Where(i => i.Type != WorkItemType.Epic && (i.ParentId is not { } parent || !ids.Contains(parent))).OrderBy(Order)];
    }

    /// <summary>All epics, alphabetically.</summary>
    public IReadOnlyList<WorkItem> Epics { get; }

    /// <summary>The items that aren't part of an epic (directly or through their parents).</summary>
    public IReadOnlyList<WorkItem> Loose { get; }

    /// <summary>The expanded epic, or <c>null</c> when the repository hub is in the middle.</summary>
    public WorkItem? Center { get; private set; }

    public IReadOnlyList<WorkItemMapNode> Nodes { get; private set; } = [];

    public IReadOnlyList<WorkItemMapEdge> Edges { get; private set; } = [];

    /// <summary>The epic that is expanded when the URL doesn't say: the first open one, or else the first one.</summary>
    public static Guid? DefaultEpic(IReadOnlyList<WorkItem> items)
    {
        var epics = items.Where(i => i.Type == WorkItemType.Epic).OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.CreatedAt).ToList();
        return (epics.FirstOrDefault(e => e.Status != WorkItemStatus.Done) ?? epics.FirstOrDefault())?.Id;
    }

    /// <param name="expandedEpic">The epic to put in the middle; when it is <c>null</c> or not an epic of <paramref name="items"/>, the repository hub is.</param>
    /// <param name="width">The width of the map in pixels.</param>
    /// <param name="height">The height of the map in pixels.</param>
    public static WorkItemMapLayout Create(IReadOnlyList<WorkItem> items, Guid? expandedEpic, double width = DefaultWidth, double height = DefaultHeight)
    {
        var layout = new WorkItemMapLayout(items, width, height);
        layout.Center = layout.Epics.FirstOrDefault(e => e.Id == expandedEpic);
        if (layout.Center is { } center)
        {
            layout.PlaceEpic(center);
        }
        else
        {
            layout.PlaceHub();
        }
        foreach (var placement in layout._placements.Where(p => p.Depth > 0))
        {
            placement.Turn = layout.Warp(placement.Angle, placement.Box);
        }
        layout.Fit();
        layout.Emit();
        return layout;
    }

    /// <summary>The number of items below an item, at any depth.</summary>
    public int DescendantCount(Guid id)
    {
        var count = 0;
        foreach (var child in _children[id])
        {
            count += 1 + DescendantCount(child.Id);
        }
        return count;
    }

    /// <summary>How far the items below an item (at any depth) are, by status: accepted includes in progress, as on the overview.</summary>
    public WorkItemMapProgress Progress(Guid id)
    {
        int draft = 0, accepted = 0, done = 0;
        Count(id);
        return new WorkItemMapProgress(draft, accepted, done);

        void Count(Guid parent)
        {
            foreach (var child in _children[parent])
            {
                switch (child.Status)
                {
                    case WorkItemStatus.Draft:
                        draft++;
                        break;
                    case WorkItemStatus.Done:
                        done++;
                        break;
                    default:
                        accepted++;
                        break;
                }
                Count(child.Id);
            }
        }
    }

    /// <summary>How many items of each type are below an item, at any depth, in type order.</summary>
    public IReadOnlyList<(WorkItemType Type, int Count)> DescendantsByType(Guid id)
    {
        var counts = new Dictionary<WorkItemType, int>();
        Count(id);
        return [.. counts.OrderBy(c => c.Key).Select(c => (c.Key, c.Value))];

        void Count(Guid parent)
        {
            foreach (var child in _children[parent])
            {
                counts[child.Type] = counts.GetValueOrDefault(child.Type) + 1;
                Count(child.Id);
            }
        }
    }

    private void PlaceEpic(WorkItem epic)
    {
        var root = new Placement(epic, WorkItemMapRole.Center, 0, null, 0, 0);
        _placements.Add(root);

        // The first child goes on the left, centred there, and the others follow clockwise: one child sits on the left and two sit
        // left and right, because the map is wider than it is high and a chain of single children has the most room sideways.
        var first = _children[epic.Id].OrderBy(Order).FirstOrDefault();
        var firstSpan = first is null ? 0 : Math.PI * 2 * Weight(first.Id) / Math.Max(1, _children[epic.Id].Sum(c => Weight(c.Id)));
        PlaceChildren(root, Math.PI - firstSpan / 2, Math.PI * 2, MaxDepth(epic.Id));
    }

    private void PlaceChildren(Placement parent, double start, double span, int maxDepth)
    {
        var children = _children[parent.Item!.Id].OrderBy(Order).ToList();
        var total = children.Sum(c => Weight(c.Id));
        var depth = parent.Depth + 1;
        var placed = new List<(Placement Placement, double Span)>();
        foreach (var child in children)
        {
            var childSpan = span * Weight(child.Id) / total;
            var placement = new Placement(child, WorkItemMapRole.Child, depth, parent, start + childSpan / 2, Radius(depth, maxDepth));
            _placements.Add(placement);
            placed.Add((placement, childSpan));
            PlaceChildren(placement, start, childSpan, maxDepth);
            start += childSpan;
        }

        // A child straight behind its parent can't clear the parent's label without leaving no room for the ring outside it, so the
        // parent moves half that child's slot aside and its children fan out on both sides. A single child has room enough further out.
        if (parent.Depth > 0 && placed.Count >= 3
            && placed.FirstOrDefault(c => Math.Abs(c.Placement.Angle - parent.Angle) < 1e-9) is { Placement: not null } behind)
        {
            parent.Angle += behind.Span / 2;
        }
    }

    private void PlaceHub()
    {
        var hub = new Placement(null, WorkItemMapRole.Center, 0, null, 0, 0);
        _placements.Add(hub);

        var epicRadius = Loose.Count > 0 ? 0.55 : 0.85;
        for (var i = 0; i < Epics.Count; i++)
        {
            _placements.Add(new Placement(Epics[i], WorkItemMapRole.Epic, 1, hub, -Math.PI / 2 + Math.PI * 2 * i / Epics.Count, epicRadius));
        }

        // Half a step further than the epics, so they don't line up behind them; without epics they take the inner ring.
        var looseRadius = Epics.Count > 0 ? 1.0 : 0.75;
        var offset = Epics.Count > 0 ? Math.PI / Math.Max(Loose.Count, 1) : 0;
        for (var i = 0; i < Loose.Count; i++)
        {
            _placements.Add(new Placement(Loose[i], WorkItemMapRole.Loose, 2, null, -Math.PI / 2 + offset + Math.PI * 2 * i / Loose.Count, looseRadius));
        }
    }

    /// <summary>
    /// Fits the rings from the inside out. First every node of a ring finds its own radius, from just outside its parent to as far as the
    /// rings outside it allow: the one closest to the evenly spread radius at which it clears the map's edges and the nodes fitted
    /// before nearly as well as anywhere, so only the nodes that need it move out. Then, when the ring's nodes overlap each other,
    /// every other node moves off the ring if that helps. The ring's nodes get the largest common size at which nothing overlaps;
    /// rings that still don't fit at <see cref="MinScale"/> hide their labels.
    /// </summary>
    private void Fit()
    {
        var rings = _placements.Where(p => p.Depth > 0).GroupBy(p => (p.Role, p.Depth)).OrderBy(g => g.Key.Depth).Select(g => g.ToList()).ToList();
        var previous = 0d;
        for (var index = 0; index < rings.Count; index++)
        {
            var ring = rings[index];
            var outside = rings.Count - index - 1;
            var highest = Math.Max(ring[0].BaseRadius, 1 - outside * RingGap);
            var fitted = _placements.Where(p => p.Fitted).ToList();

            foreach (var placement in ring)
            {
                var floor = placement.Parent is { Depth: > 0 } parent ? parent.Radius + ParentGap : previous > 0 ? previous + RingGap : 0.3;
                var lowest = Math.Min(placement.BaseRadius, floor);
                if (placement.Parent is { Depth: > 0 })
                {
                    lowest = floor;
                }
                var candidates = new List<(double Scale, double Radius)>();
                for (var radius = lowest; radius <= Math.Max(highest, lowest) + 0.0001; radius += 0.03)
                {
                    placement.Radius = radius;
                    candidates.Add((NodeScale(placement, fitted), radius));
                }
                var top = Math.Min(candidates.Max(c => c.Scale), 1);
                var tolerance = outside > 0 ? 0.1 : 0.02;
                placement.Radius = candidates.Where(c => c.Scale >= top - tolerance).MinBy(c => Math.Abs(c.Radius - placement.BaseRadius)).Radius;
                placement.FittedRadius = placement.Radius;
            }

            var scale = RingScale(ring, fitted);
            if (scale < 0.9 && ring.Count >= 6)
            {
                var order = ring.OrderBy(p => Normalize(p.Angle)).ToList();
                for (var i = 1; i < order.Count; i += 2)
                {
                    var limit = Math.Max(highest, order[i].FittedRadius);
                    order[i].Radius = order[i].FittedRadius + (order[i].FittedRadius + Stagger > limit + 0.0001 ? -Stagger : Stagger);
                }
                var staggered = RingScale(ring, fitted);
                if (staggered > scale + 0.04)
                {
                    scale = staggered;
                }
                else
                {
                    ring.ForEach(p => p.Radius = p.FittedRadius);
                }
            }

            // Only the nodes that don't fit with their label even at the smallest size lose it; the rest of the ring keeps theirs.
            // Nodes that still don't fit get the smallest size, without making the rest of the ring that small too.
            var hopeless = new List<Placement>();
            if (scale < MinScale)
            {
                ring.Where(p => LabelScale(p, ring, fitted) < MinScale).ToList().ForEach(p => p.Labelled = false);
                hopeless = [.. ring.Where(p => LabelScale(p, ring, fitted) < MinScale)];
                var rest = ring.Except(hopeless).ToList();
                scale = rest.Count > 0 ? RingScale(rest, fitted) : MinScale;
            }
            ring.ForEach(p =>
            {
                p.Scale = hopeless.Contains(p) ? MinScale : Math.Clamp(scale, MinScale, 1);
                p.Crowded = !p.Labelled;
                p.Fitted = true;
            });
            previous = ring.Max(p => p.Radius);
        }
    }

    /// <summary>The largest scale (up to 1) at which one node stays on the map and clears the nodes fitted before.</summary>
    private double NodeScale(Placement placement, List<Placement> fitted)
    {
        var scale = Math.Min(1, EdgeScale(placement));
        foreach (var other in fitted)
        {
            scale = Math.Min(scale, PairScale(placement, other, other.Scale));
        }
        return scale;
    }

    /// <summary>
    /// The largest scale (up to 1) at which the ring's nodes stay on the map and overlap neither each other nor the nodes fitted before.
    /// </summary>
    /// <summary>The largest scale at which one node of a ring clears everything: the nodes fitted before and the rest of its ring.</summary>
    private double LabelScale(Placement placement, List<Placement> ring, List<Placement> fitted)
    {
        var scale = NodeScale(placement, fitted);
        foreach (var other in ring)
        {
            if (!ReferenceEquals(other, placement))
            {
                scale = Math.Min(scale, PairScale(placement, other, null));
            }
        }
        return scale;
    }

    private double RingScale(List<Placement> ring, List<Placement> fitted)
    {
        var scale = 1d;
        for (var i = 0; i < ring.Count; i++)
        {
            scale = Math.Min(scale, NodeScale(ring[i], fitted));
            for (var j = i + 1; j < ring.Count; j++)
            {
                scale = Math.Min(scale, PairScale(ring[i], ring[j], null));
            }
        }
        return scale;
    }

    private double EdgeScale(Placement placement)
    {
        var (x, y) = Pixels(placement);
        var box = placement.Box;
        return Math.Min(Math.Min(x, _width - x) / (box.Width / 2), Math.Min(y / box.Above, (_height - y) / box.Below));
    }

    /// <summary>
    /// The largest scale of <paramref name="a"/> at which its box doesn't overlap the box of <paramref name="b"/>: at the same scale when
    /// <paramref name="bScale"/> is <c>null</c> (both are on the ring being fitted), otherwise with <paramref name="b"/> at <paramref name="bScale"/>.
    /// A box is centred horizontally on its point and reaches from half the badge above it to the label below it,
    /// so two boxes are apart when they are apart horizontally or vertically.
    /// </summary>
    private double PairScale(Placement a, Placement b, double? bScale)
    {
        var (ax, ay) = Pixels(a);
        var (bx, by) = Pixels(b);
        var boxA = a.Box;
        var boxB = b.Box;
        var dx = Math.Abs(ax - bx);
        var dy = Math.Abs(ay - by);
        var aIsUpper = ay <= by;
        if (bScale is not { } other)
        {
            var vertical = aIsUpper ? dy / (boxA.Below + boxB.Above) : dy / (boxB.Below + boxA.Above);
            return Math.Max(dx / ((boxA.Width + boxB.Width) / 2), vertical);
        }
        var horizontalFixed = (dx - other * boxB.Width / 2) / (boxA.Width / 2);
        var verticalFixed = aIsUpper ? (dy - other * boxB.Above) / boxA.Below : (dy - other * boxB.Below) / boxA.Above;
        return Math.Max(horizontalFixed, verticalFixed);
    }

    private void Emit()
    {
        var nodes = new List<WorkItemMapNode>();
        var edges = new List<WorkItemMapEdge>();
        foreach (var placement in _placements)
        {
            var (x, y) = Point(placement.Turn, placement.Radius);
            var parent = placement.Parent;
            var (fromX, fromY) = parent is null ? (CenterX, CenterY) : Point(parent.Turn, parent.Radius);
            if (placement.Item is { } item)
            {
                nodes.Add(new WorkItemMapNode(item, placement.Role, placement.Depth, x, y, fromX, fromY, DescendantCount(item.Id), placement.Scale, placement.Crowded));
            }
            if (parent is not null)
            {
                var key = $"{parent.Item?.Id.ToString() ?? "hub"}-{placement.Item!.Id}";
                var type = placement.Role == WorkItemMapRole.Epic ? WorkItemType.Epic : placement.Item.Type;
                // From the middle, the line leaves straight towards the child.
                var fromTurn = parent.Depth == 0 ? placement.Turn : parent.Turn;
                edges.Add(new WorkItemMapEdge(key, Curve(fromTurn, parent.Radius, placement.Turn, placement.Radius), type, placement.Depth));
            }
        }
        Nodes = nodes;
        Edges = edges;
    }

    private int Weight(Guid id)
    {
        if (!_weights.TryGetValue(id, out var weight))
        {
            weight = Math.Max(1, _children[id].Sum(c => Weight(c.Id)));
            _weights[id] = weight;
        }
        return weight;
    }

    private int MaxDepth(Guid id)
    {
        return _children[id].Select(c => 1 + MaxDepth(c.Id)).DefaultIfEmpty(0).Max();
    }

    // The rings spread evenly up to the edge; the first one keeps room for the epic and its label in the middle.
    private static double Radius(int depth, int maxDepth)
    {
        if (maxDepth <= 1)
        {
            return 0.85;
        }
        return 0.42 + 0.58 * (depth - 1) / (maxDepth - 1);
    }

    private static double Normalize(double angle)
    {
        var turn = Math.PI * 2;
        return ((angle % turn) + turn) % turn;
    }

    private (double X, double Y) Pixels(Placement placement)
    {
        var (x, y) = Point(placement.Turn, placement.Radius);
        return (x * _width / 100, y * _height / 100);
    }

    /// <summary>
    /// The angle on the ellipse for an angle of the tree, so that equal steps of the tree get equal room for the ring's nodes:
    /// on a wide map the left and right of a ring are much shorter than its top and bottom, and nodes there sit above each other,
    /// which needs the height of their labels. The room is measured in node boxes (horizontally in box widths, vertically in box heights).
    /// </summary>
    private double Warp(double angle, Box box)
    {
        var key = (box.Width, box.Above + box.Below);
        if (!_warps.TryGetValue(key, out var table))
        {
            table = new double[WarpSteps + 1];
            for (var k = 1; k <= WarpSteps; k++)
            {
                var t0 = Math.PI * 2 * (k - 1) / WarpSteps;
                var t1 = Math.PI * 2 * k / WarpSteps;
                var dx = Math.Abs(Math.Cos(t1) - Math.Cos(t0)) * RadiusX * _width / 100 / key.Width;
                var dy = Math.Abs(Math.Sin(t1) - Math.Sin(t0)) * RadiusY * _height / 100 / key.Item2;
                table[k] = table[k - 1] + Math.Max(dx, dy);
            }
            for (var k = 1; k <= WarpSteps; k++)
            {
                table[k] /= table[WarpSteps];
            }
            _warps[key] = table;
        }

        var turn = Math.PI * 2;
        var fraction = Normalize(angle) / turn;
        var index = Array.BinarySearch(table, fraction);
        if (index >= 0)
        {
            return turn * index / WarpSteps;
        }
        var upper = Math.Min(~index, WarpSteps);
        var lower = upper - 1;
        var part = (fraction - table[lower]) / Math.Max(table[upper] - table[lower], 1e-12);
        return turn * (lower + part) / WarpSteps;
    }

    private static (double X, double Y) Point(double angle, double radius)
    {
        return (CenterX + Math.Cos(angle) * radius * RadiusX, CenterY + Math.Sin(angle) * radius * RadiusY);
    }

    // A cubic curve that leaves the parent along its own direction and reaches the child along the child's.
    private static string Curve(double fromAngle, double fromRadius, double toAngle, double toRadius)
    {
        var middle = (fromRadius + toRadius) / 2;
        var (x0, y0) = Point(fromAngle, fromRadius);
        var (x1, y1) = Point(fromAngle, middle);
        var (x2, y2) = Point(toAngle, middle);
        var (x3, y3) = Point(toAngle, toRadius);
        return string.Create(CultureInfo.InvariantCulture, $"M{x0:0.##} {y0:0.##} C{x1:0.##} {y1:0.##} {x2:0.##} {y2:0.##} {x3:0.##} {y3:0.##}");
    }

    // Structure first (features, stories), then bugs and tasks; alphabetically within a type.
    private static (WorkItemType, string) Order(WorkItem item)
    {
        return (item.Type, item.Title.ToUpperInvariant());
    }

    /// <summary>
    /// The room a node takes in pixels at its normal size, around its point: the width of its label (or badge), what is above the point
    /// (half the badge) and below it (the other half and the label: two lines for epics and the first ring, one line deeper). These match the sizes in <c>WorkItemMap.razor.css</c>,
    /// plus a little space between nodes.
    /// </summary>
    private readonly record struct Box(double Width, double Above, double Below)
    {
        public static Box For(WorkItemMapRole role, int depth, bool isHub, bool labelled)
        {
            if (isHub)
            {
                return new Box(142, 72, 72);
            }
            if (role == WorkItemMapRole.Center)
            {
                // The counts per type sit above the badge, the two-line title below it.
                return new Box(220, 46 + 8 + 18 + 6, 46 + 5 + 40 + 6);
            }
            var (size, label, lines) = role switch
            {
                WorkItemMapRole.Epic => (60d, 150d, 36d),
                WorkItemMapRole.Loose => (30d, 100d, 14d),
                _ => depth switch
                {
                    1 => (50d, 140d, 32d),
                    2 => (38d, 116d, 15d),
                    _ => (30d, 100d, 14d)
                }
            };
            return labelled
                ? new Box(Math.Max(size, label) + 10, size / 2 + 6, size / 2 + 5 + lines + 6)
                : new Box(size + 12, size / 2 + 6, size / 2 + 6);
        }
    }

    private sealed class Placement(WorkItem? item, WorkItemMapRole role, int depth, Placement? parent, double angle, double radius)
    {
        public WorkItem? Item { get; } = item;
        public WorkItemMapRole Role { get; } = role;
        public int Depth { get; } = depth;
        public Placement? Parent { get; } = parent;
        public double Angle { get; set; } = angle;
        public double BaseRadius { get; } = radius;
        public double Radius { get; set; } = radius;

        /// <summary>The radius the node found for itself, before staggering.</summary>
        public double FittedRadius { get; set; } = radius;

        /// <summary>The angle on the ellipse, see <see cref="Warp"/>.</summary>
        public double Turn { get; set; }
        public double Scale { get; set; } = 1;
        public bool Crowded { get; set; }
        public bool Labelled { get; set; } = true;

        /// <summary>Whether the node's ring has been fitted, or it is the node in the middle, which keeps its size.</summary>
        public bool Fitted { get; set; } = depth == 0;

        public Box Box => Box.For(Role, Depth, Item is null, Labelled);
    }
}
