using System.Net;
using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>Turns accepted work items into GitHub issues and hands stories, bugs and tasks to the Copilot cloud agent.</summary>
public sealed partial class WorkItemWorkflow(IWorkItemStore store, GitHubApiClient gitHub)
{
    /// <summary>
    /// Only accepts an item whose parent (if any) is accepted, so its issue can always be linked to the parent's.
    /// Creates the issue with the label of the item's type (the label is created when missing), makes it a sub-issue
    /// of the parent's issue and makes the issues of already accepted children its sub-issues. Stories, bugs and tasks
    /// are then assigned to Copilot. Linking or assignment problems are reported in the result; the issue stays.
    /// </summary>
    public async Task<WorkItemAcceptResult> AcceptAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(repositoryFullName, id, cancellationToken);
        var name = item.Type.DisplayName.ToLowerInvariant();
        if (item.Status != WorkItemStatus.Draft)
        {
            throw new DomainRuleException($"Only a draft {name} can be accepted.");
        }
        if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Description))
        {
            throw new DomainRuleException($"A {name} needs a title and a description before it can be accepted.");
        }
        if (item.Type.RequiresParent && item.ParentId is null)
        {
            throw new DomainRuleException($"Choose the parent of this {name} before accepting it.");
        }
        if (item.ParentId is { } parentId
            && await store.GetAsync(repositoryFullName, parentId, cancellationToken) is { IssueNumber: null } parent)
        {
            throw new DomainRuleException(
                $"Accept the {parent.Type.DisplayName.ToLowerInvariant()} '{parent.Title}' before accepting this {name}.");
        }

        // Claim the draft first, so concurrent accepts can't both create an issue.
        var issueSince = await store.BeginAcceptAsync(repositoryFullName, id, AcceptClaimTimeout, cancellationToken);
        var label = item.Type.Label;
        var creating = false;
        GitHubIssue issue;
        try
        {
            await gitHub.EnsureLabelAsync(repositoryFullName, label, item.Type.LabelColor, $"{item.Type.DisplayName} planned in DGHSF", cancellationToken);

            // An earlier attempt that didn't finish may have created the issue already: reuse it instead of making a duplicate.
            var existing = issueSince is { } since
                ? await gitHub.FindIssueByMarkerAsync(repositoryFullName, label, Marker(item), LookupSince(since), cancellationToken)
                : null;
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

    public async Task<WorkItemAcceptResult> AssignAgentAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(repositoryFullName, id, cancellationToken);
        if (!item.Type.CanBeAssigned)
        {
            throw new DomainRuleException($"{item.Type.PluralName} are not assigned to an agent; only user stories, bugs and tasks are.");
        }
        if (item.Status != WorkItemStatus.Accepted || item.IssueNumber is null)
        {
            throw new DomainRuleException("Only accepted items that are not assigned yet can be assigned.");
        }
        return await TryAssignAsync(item, cancellationToken);
    }

    /// <summary>Stores the current title, description and state of the item's GitHub issue; nothing is sent to GitHub.</summary>
    public async Task<WorkItemRefreshResult> RefreshFromIssueAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(repositoryFullName, id, cancellationToken);
        if (item.IssueNumber is not { } number)
        {
            throw new DomainRuleException($"Only an accepted {item.Type.DisplayName.ToLowerInvariant()} has a GitHub issue to refresh from.");
        }

        GitHubIssue issue;
        try
        {
            issue = await gitHub.GetIssueAsync(repositoryFullName, number, cancellationToken);
        }
        catch (GitHubApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new DomainRuleException($"GitHub issue #{number} no longer exists or can't be read with your account.");
        }

        var description = DescriptionFromIssue(item, issue);
        if (description.Length > WorkItem.MaxDescriptionLength)
        {
            throw new DomainRuleException($"The description of GitHub issue #{number} is longer than the {WorkItem.MaxDescriptionLength:N0} characters DGHSF can store, so nothing was refreshed.");
        }

        var assignedToCopilot = GitHubApiClient.IsAssignedToCopilot(issue);
        var (progress, progressWarning) = item.Type.CanBeAssigned
            ? await GetProgressAsync(repositoryFullName, number, issue.IsClosed, assignedToCopilot, cancellationToken)
            : (WorkItemProgressSnapshot.None, null);

        var refreshed = await store.ApplyIssueAsync(repositoryFullName, id,
            new WorkItemIssueSnapshot(issue.Id, issue.HtmlUrl, issue.Title.Trim(), description, issue.IsClosed, assignedToCopilot, progress),
            cancellationToken);
        return new(refreshed, refreshed != item, progressWarning);
    }

    /// <summary>
    /// One GraphQL request for the linked pull requests and, while the chosen one is open, one request for Copilot's
    /// Actions run on its branch.
    /// </summary>
    /// <returns>
    /// No progress (keep the stored progress) when GitHub refuses to show the pull requests, e.g. to a token without
    /// access to them; that shouldn't stop the issue itself from being refreshed. The warning says why, also when only
    /// the agent run was refused (the progress then follows from the pull request alone).
    /// </returns>
    private async Task<(WorkItemProgressSnapshot? Progress, string? Warning)> GetProgressAsync(string repositoryFullName, int issueNumber, bool issueClosed,
        bool assignedToCopilot, CancellationToken cancellationToken)
    {
        try
        {
            var pullRequest = CopilotProgressEvaluator.SelectPullRequest(await gitHub.GetLinkedPullRequestsAsync(repositoryFullName, issueNumber, cancellationToken));
            var lookup = pullRequest is { IsOpen: true }
                ? await gitHub.GetLatestCopilotRunAsync(repositoryFullName, pullRequest.HeadRefName, cancellationToken)
                : null;
            var warning = lookup is { CanReadActions: false }
                ? ActionsRefusedWarning
                : null;
            return (CopilotProgressEvaluator.Evaluate(issueClosed, assignedToCopilot, pullRequest, lookup?.Run), warning);
        }
        catch (GitHubApiException ex) when (!StopsEveryRequest(ex))
        {
            return (null, $"Copilot's progress was kept as it was, because GitHub refused to show the linked pull requests: {ex.Message}");
        }
    }

    /// <summary>The same for every item (no pull request number), so the sync can report the items of a repository together.</summary>
    public const string ActionsRefusedWarning =
        "GitHub refused to list the Actions runs, so Copilot's agent run is unknown; whether Copilot is still working follows from the pull request's title.";

    /// <summary>An expired token or the rate limit: the next requests would fail too, so it isn't hidden.</summary>
    private static bool StopsEveryRequest(GitHubApiException ex)
    {
        return ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests
            || (ex.StatusCode == HttpStatusCode.Forbidden && ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The issue body without the marker. GitHub's web editor changes line endings and <see cref="IssueBody"/> trims, so
    /// the stored text is kept when only those differ; otherwise every refresh would count as a change.
    /// </summary>
    private static string DescriptionFromIssue(WorkItem item, GitHubIssue issue)
    {
        var description = Normalized(AnyMarker().Replace(issue.Body ?? string.Empty, string.Empty));
        return description == Normalized(item.Description) ? item.Description : description;
    }

    private static string Normalized(string text)
    {
        return text.ReplaceLineEndings("\n").Trim();
    }

    /// <summary>Any DGHSF marker, including the older formats without a creation time.</summary>
    [GeneratedRegex(@"<!--\s*dghsf:.*?-->")]
    private static partial Regex AnyMarker();

    /// <summary>
    /// How long an acceptance that is going on blocks other attempts. Longer than an attempt takes (label and issue
    /// requests), so it only matters when an attempt died without releasing its claim.
    /// </summary>
    public static readonly TimeSpan AcceptClaimTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Margin for a difference between this server's clock and GitHub's when looking for issues updated since a time.</summary>
    private static readonly TimeSpan GitHubClockSkew = TimeSpan.FromMinutes(10);

    /// <summary>The time to look for issues from: a little earlier than <paramref name="since"/>, never before 1970 (e.g. for <see cref="DateTimeOffset.MinValue"/>).</summary>
    private static DateTimeOffset LookupSince(DateTimeOffset since)
    {
        return since - DateTimeOffset.UnixEpoch > GitHubClockSkew ? since - GitHubClockSkew : DateTimeOffset.UnixEpoch;
    }

    public static string IssueBody(WorkItem item)
    {
        return $"{item.Description.Trim()}\n\n{Marker(item)}\n";
    }

    /// <summary>
    /// The hidden marker in the issue body that identifies the work item. Besides the id it holds the creation time, so
    /// items of different DGHSF databases (e.g. development and production) working on one repository never share a marker.
    /// </summary>
    public static string Marker(WorkItem item)
    {
        return $"<!-- dghsf:{item.Type.Label}:{item.Id}:{item.CreatedAt.ToUnixTimeMilliseconds()} -->";
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
        // A child is only accepted after its parent, but children accepted before that rule existed may still be
        // waiting for their parent: link those from the parent's side (below).

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

    private async Task<WorkItem> FindAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken)
    {
        return await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
    }
}
