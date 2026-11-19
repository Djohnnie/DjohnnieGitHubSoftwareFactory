using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

/// <summary>
/// What makes a plan of work items for a mindmap valid: a single epic for the central idea (or the epic it is linked to
/// already) with features, and user stories in every feature, so the plan always ends in user stories. Every idea is
/// linked to at most one new item, and only ideas that aren't linked yet.
/// </summary>
internal static class MindmapPlanRules
{
    /// <summary>The work items the ideas of the map are linked to, by id; links to removed items are left out.</summary>
    public static async Task<Dictionary<Guid, WorkItem>> LinkedItemsAsync(IWorkItemStore items, Mindmap mindmap, CancellationToken cancellationToken)
    {
        var linkedIds = mindmap.Nodes.Where(n => n.WorkItemId is not null).Select(n => n.WorkItemId!.Value).ToHashSet();
        if (linkedIds.Count == 0)
        {
            return [];
        }
        return (await items.GetAllAsync(mindmap.RepositoryFullName, cancellationToken: cancellationToken))
            .Where(item => linkedIds.Contains(item.Id))
            .ToDictionary(item => item.Id);
    }

    /// <summary>The epic the central idea is linked to, if any.</summary>
    /// <exception cref="DomainRuleException">The map has no central idea, or it is linked to another type of work item.</exception>
    public static WorkItem? ExistingEpic(Mindmap mindmap, IReadOnlyDictionary<Guid, WorkItem> linked)
    {
        var root = mindmap.Root ?? throw new DomainRuleException("The mindmap has no central idea.");
        if (LinkedItem(root, linked) is not { } item)
        {
            return null;
        }
        if (item.Type != WorkItemType.Epic)
        {
            throw new DomainRuleException(
                $"The central idea is linked to a {item.Type.DisplayName.ToLowerInvariant()}, but it always becomes the epic. Link it to an epic or unlink it first.");
        }
        return item;
    }

    /// <summary>
    /// The valid items, parents before their children, at most <see cref="MindmapPlanItem.MaxPerPlan"/>. Drops items of
    /// other types or under a parent they can't be part of (with everything below them), features without user stories,
    /// and the ideas that can't be linked to the item. Empty when the new epic ends up without features.
    /// </summary>
    public static List<MindmapPlanItem> Filter(Mindmap mindmap, IReadOnlyDictionary<Guid, WorkItem> linked, IEnumerable<MindmapPlanItem> items)
    {
        var root = mindmap.Root ?? throw new DomainRuleException("The mindmap has no central idea.");
        var existingEpic = ExistingEpic(mindmap, linked);

        // Existing items can only be the parent when they are the central idea's epic or a feature linked to an idea of this map.
        var existingParents = linked.Values
            .Where(item => item.Id == existingEpic?.Id || item.Type == WorkItemType.Feature)
            .ToDictionary(item => item.Id, item => item.Type);

        var candidates = new List<MindmapPlanItem>();
        var seen = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item.Id != Guid.Empty && MindmapPlanItem.Types.Contains(item.Type) && seen.Add(item.Id))
            {
                candidates.Add(item);
            }
        }

        var result = new List<MindmapPlanItem>();
        var types = new Dictionary<Guid, WorkItemType>();
        MindmapPlanItem? epic = null;
        if (existingEpic is null)
        {
            epic = candidates.FirstOrDefault(c => c.Type == WorkItemType.Epic);
            if (epic is not { ParentId: null, ParentWorkItemId: null })
            {
                return [];
            }
            result.Add(epic);
            types[epic.Id] = WorkItemType.Epic;
        }
        candidates.RemoveAll(c => c.Type == WorkItemType.Epic);

        WorkItemType? ParentType(MindmapPlanItem item)
        {
            if (item.ParentId is { } parentId)
            {
                return item.ParentWorkItemId is null && types.TryGetValue(parentId, out var type) ? type : null;
            }
            return item.ParentWorkItemId is { } workItemId && existingParents.TryGetValue(workItemId, out var existing) ? existing : null;
        }

        // Accepts the items whose parent is known, pass after pass, so parents come before their children (and cycles never resolve).
        var progress = true;
        while (progress && result.Count < MindmapPlanItem.MaxPerPlan)
        {
            progress = false;
            foreach (var candidate in candidates.ToList())
            {
                if (result.Count >= MindmapPlanItem.MaxPerPlan)
                {
                    break;
                }
                if (ParentType(candidate) is not { } parentType || !candidate.Type.CanHaveParent(parentType))
                {
                    continue;
                }
                result.Add(candidate);
                types[candidate.Id] = candidate.Type;
                candidates.Remove(candidate);
                progress = true;
            }
        }

        // The plan ends in user stories: drop the new features without one (the epic without features drops everything).
        result.RemoveAll(item => item.Type == WorkItemType.Feature && !result.Any(child => child.ParentId == item.Id));
        if (epic is not null && !result.Any(child => child.ParentId == epic.Id))
        {
            return [];
        }

        var used = new HashSet<Guid>();
        return result
            .Select(item =>
            {
                var ideas = item.IdeaIds.Where(id => id != root.Id && mindmap.Find(id) is { } idea && LinkedItem(idea, linked) is null);
                if (ReferenceEquals(item, epic))
                {
                    ideas = ideas.Prepend(root.Id);
                }
                return item with { IdeaIds = ideas.Where(used.Add).ToList() };
            })
            .ToList();
    }

    /// <summary>What is wrong with a plan that is about to be saved, or <c>null</c> when it is valid.</summary>
    public static string? Problem(Mindmap mindmap, IReadOnlyDictionary<Guid, WorkItem> linked, IReadOnlyList<MindmapPlanItem> items)
    {
        var valid = Filter(mindmap, linked, items);
        var unchanged = valid.Count == items.Count && items.All(item => valid.Any(v =>
            v.Id == item.Id && v.ParentId == item.ParentId && v.ParentWorkItemId == item.ParentWorkItemId && v.Type == item.Type
            && v.IdeaIds.SequenceEqual(item.IdeaIds)));
        if (unchanged)
        {
            return null;
        }
        return ExistingEpic(mindmap, linked) is null && !items.Any(item => item.Type == WorkItemType.Epic)
            ? "The epic of the central idea must be saved too."
            : "The work items must end in user stories: the epic needs a feature and every feature a user story, and every item needs its parent. " +
              "An idea can only be linked to one work item; generate again when the mindmap changed.";
    }

    private static WorkItem? LinkedItem(MindmapNode idea, IReadOnlyDictionary<Guid, WorkItem> linked)
    {
        return idea.WorkItemId is { } id ? linked.GetValueOrDefault(id) : null;
    }
}
