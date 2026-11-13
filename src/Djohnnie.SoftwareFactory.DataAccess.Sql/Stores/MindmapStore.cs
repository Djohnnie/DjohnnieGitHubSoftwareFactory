using System.Text.Json;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

/// <summary>The mindmaps of each repository; a map's ideas are stored as one JSON document in its row.</summary>
public sealed class MindmapStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IMindmapStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<MindmapSummary>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var mindmaps = await db.Mindmaps.AsNoTracking()
            .Where(m => m.Repository.FullName == normalized)
            .ToListAsync(cancellationToken);
        return mindmaps
            .Select(m =>
            {
                var nodes = Deserialize(m.Nodes);
                return new MindmapSummary(m.Id, m.Title, nodes.Count, nodes.Count(n => n.IsDone), m.UpdatedAt);
            })
            .OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Id)
            .ToList();
    }

    public async Task<Mindmap?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var mindmap = await db.Mindmaps.AsNoTracking().Include(m => m.Repository)
            .SingleOrDefaultAsync(m => m.Id == id && m.Repository.FullName == normalized, cancellationToken);
        return mindmap is null ? null : ToContract(mindmap);
    }

    public async Task<Mindmap> SaveAsync(Mindmap value, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        Entities.Mindmap mindmap;
        if (value.Id == Guid.Empty)
        {
            mindmap = new Entities.Mindmap
            {
                Repository = await db.GetOrAddRepositoryAsync(value.RepositoryFullName, cancellationToken),
                CreatedAt = now
            };
            db.Mindmaps.Add(mindmap);
        }
        else
        {
            var normalized = Entities.Repository.Normalize(value.RepositoryFullName);
            mindmap = await db.Mindmaps.Include(m => m.Repository)
                .SingleOrDefaultAsync(m => m.Id == value.Id && m.Repository.FullName == normalized, cancellationToken)
                ?? throw new DomainRuleException("The mindmap no longer exists.");
        }

        mindmap.Title = value.Title;
        mindmap.Nodes = JsonSerializer.Serialize(value.Nodes.Select(n => new NodeData(n.Id, n.ParentId, n.Text, n.X, n.Y, n.IsDone, n.WorkItemId)), JsonOptions);
        mindmap.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return ToContract(mindmap);
    }

    public async Task<Mindmap> LinkAsync(string repositoryFullName, Guid mindmapId, Guid ideaId, Guid? workItemId, bool markDone, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var mindmap = await db.Mindmaps.Include(m => m.Repository)
            .SingleOrDefaultAsync(m => m.Id == mindmapId && m.Repository.FullName == normalized, cancellationToken)
            ?? throw new DomainRuleException("The mindmap no longer exists.");
        var nodes = Deserialize(mindmap.Nodes);
        var index = nodes.FindIndex(n => n.Id == ideaId);
        if (index < 0)
        {
            throw new DomainRuleException("The idea no longer exists in the mindmap. Was it removed without saving?");
        }

        nodes[index] = nodes[index] with { WorkItemId = workItemId, IsDone = nodes[index].IsDone || markDone };
        mindmap.Nodes = JsonSerializer.Serialize(nodes, JsonOptions);
        mindmap.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return ToContract(mindmap);
    }

    public async Task<List<MindmapIdeaLink>> GetLinkedIdeasAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var marker = workItemId.ToString();
        var mindmaps = await db.Mindmaps.AsNoTracking()
            .Where(m => m.Repository.FullName == normalized && m.Nodes.Contains(marker))
            .ToListAsync(cancellationToken);
        return mindmaps
            .SelectMany(m => Deserialize(m.Nodes)
                .Where(n => n.WorkItemId == workItemId)
                .Select(n => new MindmapIdeaLink(m.Id, m.Title, n.Id, n.Text, n.IsDone)))
            .OrderBy(link => link.MindmapTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(link => link.IdeaText, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<int> MarkWorkItemDoneAsync(string repositoryFullName, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var marker = workItemId.ToString();
        // The ids are in the JSON, so this only loads the maps that mention the item.
        var mindmaps = await db.Mindmaps
            .Where(m => m.Repository.FullName == normalized && m.Nodes.Contains(marker))
            .ToListAsync(cancellationToken);
        var marked = 0;
        foreach (var mindmap in mindmaps)
        {
            var nodes = Deserialize(mindmap.Nodes);
            var changed = false;
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].WorkItemId == workItemId && !nodes[i].IsDone)
                {
                    nodes[i] = nodes[i] with { IsDone = true };
                    changed = true;
                    marked++;
                }
            }
            if (changed)
            {
                mindmap.Nodes = JsonSerializer.Serialize(nodes, JsonOptions);
                mindmap.UpdatedAt = timeProvider.GetUtcNow();
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return marked;
    }

    public async Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        var mindmap = await db.Mindmaps.SingleOrDefaultAsync(m => m.Id == id && m.Repository.FullName == normalized, cancellationToken);
        if (mindmap is not null)
        {
            db.Mindmaps.Remove(mindmap);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static Mindmap ToContract(Entities.Mindmap mindmap)
    {
        return new Mindmap
        {
            Id = mindmap.Id,
            RepositoryFullName = mindmap.Repository.FullName,
            Nodes = Deserialize(mindmap.Nodes)
                .Select(n => new MindmapNode { Id = n.Id, ParentId = n.ParentId, Text = n.Text, X = n.X, Y = n.Y, IsDone = n.IsDone, WorkItemId = n.WorkItemId })
                .ToList(),
            CreatedAt = mindmap.CreatedAt,
            UpdatedAt = mindmap.UpdatedAt
        };
    }

    private static List<NodeData> Deserialize(string json)
    {
        return JsonSerializer.Deserialize<List<NodeData>>(json, JsonOptions) ?? [];
    }

    /// <summary>The stored form of an idea; keep the property names, they are in the database.</summary>
    private sealed record NodeData(Guid Id, Guid? ParentId, string Text, double X, double Y, bool IsDone, Guid? WorkItemId = null);
}
