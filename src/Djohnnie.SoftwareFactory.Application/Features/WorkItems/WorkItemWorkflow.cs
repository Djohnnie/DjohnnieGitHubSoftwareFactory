using System.Net;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>Turns accepted work items into GitHub issues and hands stories, bugs and tasks to the Copilot cloud agent.</summary>
public sealed class WorkItemWorkflow(IWorkItemStore store, GitHubApiClient gitHub)
{
    /// <summary>
    /// Creates the issue with the label of the item's type (the label is created when missing), makes it a sub-issue
    /// of the parent's issue and makes the issues of already accepted children its sub-issues. Stories, bugs and tasks
    /// are then assigned to Copilot. Linking or assignment problems are reported in the result; the issue stays.
    /// </summary>
    public async Task<WorkItemAcceptResult> AcceptAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(repositoryFullName, id, cancellationToken);
        var name = item.Type.DisplayName.ToLowerInvariant();
        if (item.Status != WorkItemStatus.Draft)
        {
            throw new InvalidOperationException($"Only a draft {name} can be accepted.");
        }
        if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Description))
        {
            throw new InvalidOperationException($"A {name} needs a title and a description before it can be accepted.");
        }
        if (item.Type.RequiresParent && item.ParentId is null)
        {
            throw new InvalidOperationException($"Choose the parent of this {name} before accepting it.");
        }

        // Claim the draft first, so concurrent accepts can't both create an issue.
        var issueMayExist = await store.BeginAcceptAsync(repositoryFullName, id, AcceptClaimTimeout, cancellationToken);
        var label = item.Type.Label;
        var creating = false;
        GitHubIssue issue;
        try
        {
            await gitHub.EnsureLabelAsync(repositoryFullName, label, item.Type.LabelColor, $"{item.Type.DisplayName} planned in DGHSF", cancellationToken);

            // An earlier attempt that didn't finish may have created the issue already: reuse it instead of making a duplicate.
            var existing = issueMayExist ? await gitHub.FindIssueByMarkerAsync(repositoryFullName, label, Marker(item), cancellationToken) : null;
            creating = existing is null;
            issue = existing ?? await gitHub.CreateIssueAsync(repositoryFullName, item.Title.Trim(), IssueBody(item), [label], cancellationToken);
        }
        catch (Exception ex)
        {
            await EndFailedAcceptAsync(item, creating && !IsRejectedByGitHub(ex));
            throw;
        }

        try
        {
            item = await store.MarkAcceptedAsync(repositoryFullName, id, issue.Id, issue.Number, issue.HtmlUrl, cancellationToken);
        }
        catch
        {
            await EndFailedAcceptAsync(item, issueMayExist: true);
            throw;
        }

        // Link before assigning, so Copilot sees the issue as part of its parent.
        var hierarchyError = await LinkHierarchyAsync(item, issue, cancellationToken);
        if (!item.Type.CanBeAssigned)
        {
            return new(item, null, hierarchyError);
        }
        return await TryAssignAsync(item, cancellationToken) with { HierarchyError = hierarchyError };
    }

    public async Task<WorkItemAcceptResult> AssignAgentAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(repositoryFullName, id, cancellationToken);
        if (!item.Type.CanBeAssigned)
        {
            throw new InvalidOperationException($"{item.Type.PluralName} are not assigned to an agent; only user stories, bugs and tasks are.");
        }
        if (item.Status != WorkItemStatus.Accepted || item.IssueNumber is null)
        {
            throw new InvalidOperationException("Only accepted items that are not assigned yet can be assigned.");
        }
        return await TryAssignAsync(item, cancellationToken);
    }

    /// <summary>
    /// How long an acceptance that is going on blocks other attempts. Longer than an attempt takes (label and issue
    /// requests), so it only matters when an attempt died without releasing its claim.
    /// </summary>
    public static readonly TimeSpan AcceptClaimTimeout = TimeSpan.FromMinutes(5);

    public static string IssueBody(WorkItem item)
    {
        return $"{item.Description.Trim()}\n\n{Marker(item)}\n";
    }

    /// <summary>The hidden marker in the issue body that identifies the work item.</summary>
    public static string Marker(WorkItem item)
    {
        return $"<!-- dghsf:{item.Type.Label}:{item.Id} -->";
    }

    /// <summary>GitHub refused the request (4xx), so it certainly didn't create the issue.</summary>
    private static bool IsRejectedByGitHub(Exception ex)
    {
        return ex is GitHubApiException { StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError }
            and not GitHubApiException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests };
    }

    /// <summary>
    /// Releases the claim of a failed attempt. Best effort: when this fails too, the claim expires after
    /// <see cref="AcceptClaimTimeout"/> and the next attempt still looks for the issue first.
    /// </summary>
    private async Task EndFailedAcceptAsync(WorkItem item, bool issueMayExist)
    {
        try
        {
            await store.EndFailedAcceptAsync(item.RepositoryFullName, item.Id, issueMayExist, CancellationToken.None);
        }
        catch (Exception)
        {
            // See above: the claim expires.
        }
    }

    private async Task<string?> LinkHierarchyAsync(WorkItem item, GitHubIssue issue, CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        if (item.ParentId is { } parentId
            && await store.GetAsync(item.RepositoryFullName, parentId, cancellationToken) is { IssueNumber: { } parentNumber })
        {
            await TryLinkAsync(parentNumber, issue.Number, issue.Id);
        }
        // A parent that isn't accepted yet links this issue when it is accepted (below, from the parent's side).

        var children = await store.GetAllAsync(item.RepositoryFullName, parentId: item.Id, cancellationToken: cancellationToken);
        foreach (var child in children.Where(c => c.IssueNumber is not null))
        {
            var childId = child.IssueId;
            if (childId is null)
            {
                try
                {
                    childId = (await gitHub.GetIssueAsync(item.RepositoryFullName, child.IssueNumber!.Value, cancellationToken)).Id;
                    await store.SetIssueIdAsync(item.RepositoryFullName, child.Id, childId.Value, cancellationToken);
                }
                catch (Exception ex) when (ex is GitHubApiException or HttpRequestException)
                {
                    problems.Add($"Could not look up issue #{child.IssueNumber}: {ex.Message}");
                    continue;
                }
            }
            await TryLinkAsync(issue.Number, child.IssueNumber!.Value, childId.Value);
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);

        async Task TryLinkAsync(int parentNumber, int childNumber, long childId)
        {
            try
            {
                await gitHub.AddSubIssueAsync(item.RepositoryFullName, parentNumber, childId, cancellationToken);
            }
            catch (Exception ex) when (ex is GitHubApiException or HttpRequestException)
            {
                problems.Add($"Could not make #{childNumber} a sub-issue of #{parentNumber}: {ex.Message}");
            }
        }
    }

    private async Task<WorkItemAcceptResult> TryAssignAsync(WorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            var assignment = new CopilotAssignment(item.RepositoryFullName, item.BaseBranch, item.CustomInstructions, item.CustomAgent, item.Model);
            var issue = await gitHub.AssignCopilotAsync(item.RepositoryFullName, item.IssueNumber!.Value, assignment, cancellationToken);
            if (!GitHubApiClient.IsAssignedToCopilot(issue))
            {
                return new(item, "GitHub did not assign Copilot. Check that Copilot cloud agent is enabled for this repository.");
            }
        }
        catch (Exception ex) when (ex is GitHubApiException or HttpRequestException)
        {
            return new(item, ex.Message);
        }

        return new(await store.MarkAssignedAsync(item.RepositoryFullName, item.Id, cancellationToken), null);
    }

    private async Task<WorkItem> FindAsync(string repositoryFullName, int id, CancellationToken cancellationToken)
    {
        return await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new InvalidOperationException("The work item no longer exists.");
    }
}
