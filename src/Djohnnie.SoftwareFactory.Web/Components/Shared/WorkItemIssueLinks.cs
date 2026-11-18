using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// Maps the GitHub issue of a work item to the item's page in DGHSF, so a chat reply that links an issue keeps the user in
/// DGHSF. The query, fragment and a trailing slash of the URL are ignored, e.g. <c>…/issues/12#issuecomment-1</c>.
/// </summary>
public sealed class WorkItemIssueLinks
{
    private readonly Dictionary<string, string> _pages = new(StringComparer.OrdinalIgnoreCase);

    public WorkItemIssueLinks(IEnumerable<WorkItem> items)
    {
        foreach (var item in items)
        {
            if (Normalize(item.IssueUrl) is { } issue)
            {
                _pages[issue] = AppPaths.WorkItem(item.Type, item.Id);
            }
        }
    }

    /// <summary>The page of the work item whose issue <paramref name="url"/> is, or <c>null</c>.</summary>
    public string? PageFor(string url)
    {
        return Normalize(url) is { } issue && _pages.TryGetValue(issue, out var page) ? page : null;
    }

    private static string? Normalize(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        return $"{uri.Host}{uri.AbsolutePath.TrimEnd('/')}";
    }
}
