using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Djohnnie.SoftwareFactory.Web.Repositories;

/// <summary>
/// The repository the whole app currently works on. Scoped per circuit and remembered in
/// (encrypted) browser local storage, so it survives reloads and new sessions.
/// </summary>
public sealed class RepositoryContext(
    ProtectedLocalStorage storage, ILogger<RepositoryContext> logger, RepositoryHistoryService history)
{
    private const string StorageKey = "dghsf.repository";
    private bool _loaded;

    public GitHubRepository? Current { get; private set; }

    public event Action? Changed;

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
        }

        _loaded = true;
        Changed?.Invoke();
    }

    public async Task SelectAsync(GitHubRepository repository)
    {
        await history.RecordAccessAsync(repository.FullName);
        await storage.SetAsync(StorageKey, repository);
        Current = repository;
        Changed?.Invoke();
    }
}
