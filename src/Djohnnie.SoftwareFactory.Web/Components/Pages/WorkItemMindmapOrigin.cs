using Djohnnie.SoftwareFactory.Application.Features.Assistant;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Application.Features.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Pages;

/// <summary>
/// The mindmap idea a new work item is created for (from the mindmap page, <c>?mindmap=&amp;idea=</c>). The Mindmap Work Item
/// Writer writes the item's title and description from the whole map; saving the item links it to the idea. A removed idea
/// or map only gives a warning, and without the agent (disabled, unconfigured or failing) the idea becomes the title.
/// </summary>
/// <param name="render">Renders the page again (<c>StateHasChanged</c>), so it shows that the writer is busy.</param>
public sealed class WorkItemMindmapOrigin(MindmapService mindmaps, AssistantService assistant, InternalAgentService internalAgents,
    ISnackbar snackbar, Action render)
{
    private const int MaxTitleLength = 256;

    public Mindmap? Mindmap { get; private set; }
    public MindmapNode? Idea { get; private set; }

    /// <summary>The Mindmap Work Item Writer is configured and enabled.</summary>
    public bool WriterActive { get; private set; }

    /// <summary>The writer is writing the item now.</summary>
    public bool Writing { get; private set; }

    /// <summary>The item is created for an idea that still exists.</summary>
    public bool HasIdea
    {
        get
        {
            return Mindmap is not null && Idea is not null;
        }
    }

    public void Clear()
    {
        Mindmap = null;
        Idea = null;
        WriterActive = false;
        Writing = false;
    }

    public async Task LoadAsync(string repositoryFullName, Guid? mindmapId, Guid? ideaId)
    {
        Clear();
        if (mindmapId is not { } mapId || ideaId is not { } id)
        {
            return;
        }
        try
        {
            Mindmap = await mindmaps.GetAsync(repositoryFullName, mapId);
            Idea = Mindmap?.Find(id);
            if (Idea is null)
            {
                // The map stays, so the page's back arrow still leads to it.
                snackbar.Add("The mindmap idea no longer exists, so this item won't be linked to it.", Severity.Warning);
                return;
            }
            WriterActive = assistant.IsConfigured && (await internalAgents.GetAsync(InternalAgentKeys.MindmapWorkItemWriter)).IsEnabled;
        }
        catch (Exception ex) when (WorkItemActions.IsExpected(ex))
        {
            Mindmap = null;
            Idea = null;
            snackbar.Add($"Could not load the mindmap: {ex.Message}", Severity.Warning);
        }
    }

    /// <summary>
    /// Lets the Mindmap Work Item Writer fill in the title and description of <paramref name="item"/> from the whole map,
    /// following the chosen template and the item's parent. Without the agent, or when it fails, an empty title becomes the idea.
    /// </summary>
    /// <returns>Whether the writer wrote the title and description.</returns>
    public async Task<bool> WriteAsync(WorkItem item, Guid? templateId)
    {
        if (Mindmap is not { } mindmap || Idea is not { } idea)
        {
            return false;
        }
        if (!WriterActive)
        {
            UseIdeaAsTitle(item, idea);
            return false;
        }

        Writing = true;
        render();
        try
        {
            var written = await mindmaps.GenerateWorkItemAsync(new(item.RepositoryFullName, mindmap.Id, idea.Id, item.Type, templateId, item.ParentId));
            item.Title = written.Title;
            item.Description = written.Description;
            return true;
        }
        catch (Exception ex) when (WorkItemActions.IsExpected(ex))
        {
            snackbar.Add(ex.Message, Severity.Warning);
            UseIdeaAsTitle(item, idea);
            return false;
        }
        finally
        {
            Writing = false;
        }
    }

    /// <summary>Links the just saved item to the idea; a failure is only reported, because the item is saved.</summary>
    public async Task LinkAsync(string repositoryFullName, Guid workItemId)
    {
        if (Mindmap is not { } mindmap || Idea is not { } idea)
        {
            return;
        }
        try
        {
            await mindmaps.LinkAsync(repositoryFullName, mindmap.Id, idea.Id, workItemId);
            snackbar.Add($"Linked to the idea '{idea.Text}' of the mindmap '{mindmap.Title}'.", Severity.Info);
        }
        catch (Exception ex) when (WorkItemActions.IsExpected(ex))
        {
            snackbar.Add($"Saved, but could not link it to the mindmap idea: {ex.Message}", Severity.Warning);
        }
        Clear();
    }

    private static void UseIdeaAsTitle(WorkItem item, MindmapNode idea)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            item.Title = idea.Text.Length <= MaxTitleLength ? idea.Text : idea.Text[..MaxTitleLength];
        }
    }
}
