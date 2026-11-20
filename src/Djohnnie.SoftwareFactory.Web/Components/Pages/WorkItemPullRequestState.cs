using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Web.Components.Shared;

namespace Djohnnie.SoftwareFactory.Web.Components.Pages;

/// <summary>The part of a work item's pull request that is shown.</summary>
public enum PullRequestSection
{
    Changes,
    Discussion
}

/// <summary>
/// The pull request of a work item as loaded from GitHub, with the feedback being written for Copilot. The changes and the
/// discussion tabs share it, so line comments written on the diff are sent with the feedback from the discussion. It is
/// loaded in the background as soon as the page shows the pull request's tabs, so their counts fill in by themselves; opening
/// a tab while that load runs waits for it instead of loading again (<see cref="EnsureLoadedAsync"/>).
/// </summary>
public sealed class WorkItemPullRequestState(WorkItemService workItemService)
{
    /// <summary>Above this many diff lines in total, files start collapsed so the page stays responsive.</summary>
    private const int ExpandAllLimit = 2000;

    /// <summary>The load that runs now, which opening a tab waits for; see <see cref="Loading"/>.</summary>
    private Task? _load;

    /// <summary>
    /// Goes up with every load and when the state follows another pull request, so a load that was overtaken (by a newer
    /// one, or because the page moved on to another pull request) leaves the state alone when it ends.
    /// </summary>
    private int _version;

    /// <summary>Raised when the state changes, so every view of it (the tabs and their badges) renders again.</summary>
    public event Action? Changed;

    /// <summary>The number of the pull request this state belongs to.</summary>
    public int? Number { get; private set; }

    public PullRequestChanges? Changes { get; private set; }
    public IReadOnlyDictionary<string, IReadOnlyList<DiffLine>> Patches { get; private set; } = new Dictionary<string, IReadOnlyList<DiffLine>>();
    public HashSet<string> Expanded { get; } = [];
    public List<PullRequestLineComment> Pending { get; } = [];
    public string? Body { get; set; }
    public bool RequestChanges { get; set; }

    /// <summary>How the pull request gets merged.</summary>
    public PullRequestMergeMethod MergeMethod { get; set; }

    /// <summary>The comment or review that was sent last, so it can be followed on GitHub.</summary>
    public string? SentUrl { get; set; }
    public bool Loading { get; private set; }
    public bool Sending { get; set; }
    public string? Error { get; private set; }

    public bool CanSendFeedback
    {
        get
        {
            return Changes is { IsOpen: true };
        }
    }

    /// <summary>Whether the pull request should be loaded now: it isn't yet, and loading didn't just fail.</summary>
    public bool NeedsLoading
    {
        get
        {
            return Changes is null && !Loading && Error is null;
        }
    }

    /// <summary>Another pull request (e.g. after a refresh): what was loaded and commented belongs to the old one.</summary>
    public void Follow(int? pullRequestNumber)
    {
        if (Number == pullRequestNumber)
        {
            return;
        }
        Number = pullRequestNumber;
        _version++;
        _load = null;
        Loading = false;
        Changes = null;
        Patches = new Dictionary<string, IReadOnlyList<DiffLine>>();
        Expanded.Clear();
        Pending.Clear();
        Body = null;
        RequestChanges = false;
        SentUrl = null;
        Error = null;
    }

    /// <summary>
    /// Makes sure the pull request is loaded without loading it twice: waits for the load that runs (e.g. the one started in
    /// the background), starts one when nothing was loaded yet, and does nothing once it is loaded or loading failed (the
    /// reload button tries again).
    /// </summary>
    public Task EnsureLoadedAsync(string repositoryName, Guid workItemId)
    {
        if (Loading && _load is { } running)
        {
            return running;
        }
        return NeedsLoading ? LoadAsync(repositoryName, workItemId) : Task.CompletedTask;
    }

    /// <summary>Loads the pull request again, e.g. after feedback was sent; a load that still runs is overtaken.</summary>
    public Task LoadAsync(string repositoryName, Guid workItemId)
    {
        var load = FetchAsync(repositoryName, workItemId, ++_version);
        // A load that ended right away (it failed before reaching GitHub) has nothing to wait for any more.
        _load = Loading ? load : null;
        return load;
    }

    private async Task FetchAsync(string repositoryName, Guid workItemId, int version)
    {
        Loading = true;
        Error = null;
        NotifyChanged();
        try
        {
            var changes = await workItemService.GetPullRequestChangesAsync(repositoryName, workItemId);
            if (version != _version)
            {
                return;
            }
            var patches = changes.Files.ToDictionary(f => f.FileName, f => DiffPatch.Parse(f.Patch));
            var firstLoad = Changes is null;
            Changes = changes;
            Patches = patches;
            if (firstLoad && patches.Values.Sum(p => p.Count) <= ExpandAllLimit)
            {
                Expanded.UnionWith(patches.Keys);
            }
            // Comments on lines that are no longer in the diff couldn't be sent.
            Pending.RemoveAll(c => !patches.TryGetValue(c.Path, out var lines) || !lines.Any(l => l.CanComment && l.Side == c.Side && l.CommentLine == c.Line));
        }
        catch (Exception ex) when (WorkItemActions.IsExpected(ex))
        {
            if (version == _version)
            {
                Error = $"Could not load the pull request: {ex.Message}";
            }
        }
        finally
        {
            if (version == _version)
            {
                Loading = false;
                _load = null;
                NotifyChanged();
            }
        }
    }

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
