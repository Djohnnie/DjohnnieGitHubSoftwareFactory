using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

internal static class WorkItemAncestors
{
    extension(IWorkItemStore store)
    {
        /// <summary>The item with <paramref name="parentId"/> and every item above it, closest first; empty without a parent.</summary>
        public async Task<List<WorkItem>> GetAncestorsAsync(string repositoryFullName, Guid? parentId, CancellationToken cancellationToken)
        {
            var ancestors = new List<WorkItem>();
            var id = parentId;
            // The hierarchy is at most epic > feature > story; the visited check only guards against a corrupt loop.
            while (id is { } current && ancestors.All(a => a.Id != current)
                && await store.GetAsync(repositoryFullName, current, cancellationToken) is { } item)
            {
                ancestors.Add(item);
                id = item.ParentId;
            }
            return ancestors;
        }
    }
}
