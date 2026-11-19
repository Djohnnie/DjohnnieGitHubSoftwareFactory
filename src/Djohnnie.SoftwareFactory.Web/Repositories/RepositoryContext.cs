using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Djohnnie.SoftwareFactory.Web.Repositories;

/// <summary>
/// The repository the whole app currently works on. Scoped per circuit and remembered in
/// (encrypted) browser local storage, so it survives reloads and new sessions.
/// </summary>
public sealed class RepositoryContext(
    ProtectedLocalStorage storage, ILogger<RepositoryContext> logger, RepositoryService history, GitHubService gitHub)
{
    private const string StorageKey = "dghsf.repository";
    private bool _loaded;

    public GitHubRepository? Current { get; private set; }

    /// <summary>
    /// The sync found <see cref="Current"/> removed from GitHub (it is archived in the database). Everything DGHSF stored
    /// can still be viewed, but everything that needs GitHub is disabled.
    /// </summary>
    public bool IsRemovedFromGitHub { get; private set; }

    /// <summary>When the sync found <see cref="Current"/> removed from GitHub; <c>null</c> unless <see cref="IsRemovedFromGitHub"/>.</summary>
    public DateTimeOffset? RemovedFromGitHubAt { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// A stand-in for a repository that GitHub no longer knows, made from what the database stored. It has no links:
    /// <see cref="GitHubRepository.HtmlUrl"/> is empty and the owner has no avatar.
    /// </summary>
    public static GitHubRepository StandIn(RepositorySummary repository)
    {
        var owner = repository.FullName[..repository.FullName.IndexOf('/')];
        return new GitHubRepository(0, repository.Name, repository.FullName, new GitHubOwner(owner, null), string.Empty,
            null, false, false, false, null, null, null);
    }

    /// <summary>Restores the last selected repository. Requires an interactive render (JS interop).</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            var result = await storage.GetAsync<GitHubRepository>(StorageKey);
            Current = result.Success ? result.Value : null;
        }
        catch (Exception ex)
        {
            // Unreadable (e.g. data protection keys changed): just ask for a repository again.
            logger.LogWarning(ex, "Could not restore the selected repository.");
            Current = null;
        }

        if (Current is { } repository)
        {
            try
            {
                await history.RecordAccessAsync(repository.FullName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not record access to repository {Repository}.", repository.FullName);
            }
            await LoadArchivedStateAsync(repository.FullName);
        }

        _loaded = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Replaces the restored repository with GitHub's current data (e.g. a changed default branch, visibility or name),
    /// since the copy in local storage is only as fresh as the last selection. Keeps the stored copy when GitHub can't be reached.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (Current is not { } restored || IsRemovedFromGitHub)
        {
            // GitHub doesn't know a removed repository anymore: the stored copy is all there is.
            return;
        }

        GitHubRepository fresh;
        try
        {
            fresh = await gitHub.GetRepositoryAsync(restored.FullName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not refresh repository {Repository}; the stored copy is used.", restored.FullName);
            return;
        }

        // Another repository may have been selected in the meantime.
        if (!ReferenceEquals(Current, restored) || fresh == restored)
        {
            return;
        }

        Current = fresh;
        try
        {
            await storage.SetAsync(StorageKey, fresh);
            if (!string.Equals(fresh.FullName, restored.FullName, StringComparison.OrdinalIgnoreCase))
            {
                await history.RecordAccessAsync(fresh.FullName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not store the refreshed repository {Repository}.", fresh.FullName);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Selects a repository GitHub just returned. One the sync archived is restored, since GitHub evidently knows it again
    /// (e.g. after the sync token couldn't see it).
    /// </summary>
    public async Task SelectAsync(GitHubRepository repository)
    {
        await history.RecordAccessAsync(repository.FullName);
        await storage.SetAsync(StorageKey, repository);
        Current = repository;
        try
        {
            await history.SetArchivedAsync(repository.FullName, false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not restore repository {Repository}, which GitHub knows again.", repository.FullName);
        }
        IsRemovedFromGitHub = false;
        RemovedFromGitHubAt = null;
        Changed?.Invoke();
    }

    /// <summary>Selects a repository that was removed from GitHub, to view what DGHSF stored about it.</summary>
    public async Task SelectArchivedAsync(RepositorySummary repository)
    {
        var standIn = StandIn(repository);
        await history.RecordAccessAsync(repository.FullName);
        await storage.SetAsync(StorageKey, standIn);
        Current = standIn;
        IsRemovedFromGitHub = true;
        RemovedFromGitHubAt = repository.ArchivedAt;
        Changed?.Invoke();
    }

    /// <summary>Reads whether the sync archived the repository; when the database can't tell, GitHub is assumed to know it.</summary>
    private async Task LoadArchivedStateAsync(string fullName)
    {
        try
        {
            var known = await history.FindAsync(fullName);
            IsRemovedFromGitHub = known?.IsArchived == true;
            RemovedFromGitHubAt = known?.ArchivedAt;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not check whether repository {Repository} was removed from GitHub.", fullName);
            IsRemovedFromGitHub = false;
            RemovedFromGitHubAt = null;
        }
    }
}
