using System.Net;
using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>
/// Turns accepted work items into GitHub issues and hands stories, bugs and tasks to their agent: the Copilot cloud agent
/// (assigned on GitHub) or the DGHSF agent worker (a run in <see cref="IAgentRunStore"/> that the worker picks up).
/// </summary>
public sealed partial class WorkItemWorkflow(IWorkItemStore store, GitHubApiClient gitHub, IAgentRunStore agentRuns, IOptions<AgentRunOptions> agentRunOptions)
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
        if (!item.Status.IsDraft)
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
        // The whole hierarchy above the item, closest first: the issue body lists their issues.
        var ancestors = await store.GetAncestorsAsync(repositoryFullName, item.ParentId, cancellationToken);
        if (ancestors is [{ IssueNumber: null } parent, ..])
        {
            throw new DomainRuleException(
                $"Accept the {parent.Type.DisplayName.ToLowerInvariant()} '{parent.Title}' before accepting this {name}.");
        }

        // Claim the draft first, so concurrent accepts can't both create an issue.
        var issueSince = await store.BeginAcceptAsync(repositoryFullName, id, AcceptClaimTimeout, cancellationToken);
        var label = item.Type.Label;
        var creating = false;
        GitHubIssue? issue = null;
        try
        {
            await gitHub.EnsureLabelAsync(repositoryFullName, label, item.Type.LabelColor, $"{item.Type.DisplayName} planned in DGHSF", cancellationToken);

            // An earlier attempt that didn't finish may have created the issue already: reuse it instead of making a duplicate.
            var existing = issueSince is { } since
                ? await gitHub.FindIssueByMarkerAsync(repositoryFullName, label, Marker(item), LookupSince(since), cancellationToken)
                : null;
            creating = existing is null;
            issue = existing ?? await gitHub.CreateIssueAsync(repositoryFullName, item.Title.Trim(), IssueBody(item, ancestors), [label], cancellationToken);

            // Still part of the attempt, so the item only becomes accepted (and is assigned) once the agent can read them.
            await PostTechnicalDetailsAsync(item, issue, existing is not null, cancellationToken);
        }
        catch (Exception ex)
        {
            await EndFailedAcceptAsync(item, issue is not null || (creating && !IsRejectedByGitHub(ex)));
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

    /// <summary>
    /// Starts another run of the DGHSF agent worker on an item it already worked on, e.g. after a failed run. The new run
    /// branches from the base branch again and opens its own pull request.
    /// </summary>
    public async Task<AgentRun> StartWorkerRunAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await FindWorkerItemAsync(repositoryFullName, id, cancellationToken);
        return await agentRuns.CreateAsync(repositoryFullName, id, item.Model, agentRunOptions.Value.MaxAttempts, cancellationToken);
    }

    /// <summary>
    /// Starts a revision run of the DGHSF agent worker on the newest open pull request it opened for the item: it addresses
    /// the feedback written on it since the agent's last run there and pushes to the same branch.
    /// </summary>
    public async Task<AgentRun> StartRevisionRunAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await FindWorkerItemAsync(repositoryFullName, id, cancellationToken);
        var opened = (await agentRuns.GetForWorkItemAsync(repositoryFullName, id, cancellationToken))
            .FirstOrDefault(r => r is { Kind: AgentRunKind.Implement, Status: AgentRunStatus.Succeeded, PullRequestNumber: not null })
            ?? throw new DomainRuleException("The DGHSF agent worker hasn't opened a pull request for this item yet.");
        var number = opened.PullRequestNumber!.Value;

        GitHubPullRequestDetails pullRequest;
        try
        {
            pullRequest = await gitHub.GetPullRequestAsync(repositoryFullName, number, cancellationToken);
        }
        catch (GitHubApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new DomainRuleException($"Pull request #{number} no longer exists or can't be read with your account.");
        }
        if (!pullRequest.IsOpen)
        {
            throw new DomainRuleException($"Pull request #{number} is {(pullRequest.MergedAt is null ? "closed" : "merged")}, so the agent can no longer revise it.");
        }

        var branch = pullRequest.Head?.Ref ?? opened.Branch
            ?? throw new DomainRuleException($"The branch of pull request #{number} is unknown.");
        return await agentRuns.CreateRevisionAsync(repositoryFullName, id, item.Model, agentRunOptions.Value.MaxAttempts, branch, number, pullRequest.HtmlUrl,
            cancellationToken);
    }

    /// <summary>A user story, bug or task in progress on the DGHSF agent worker, which can start another run.</summary>
    private async Task<WorkItem> FindWorkerItemAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken)
    {
        var item = await FindAsync(repositoryFullName, id, cancellationToken);
        if (item.Executor != AgentExecutor.DghsfWorker || !item.Type.CanBeAssigned)
        {
            throw new DomainRuleException("Only user stories, bugs and tasks that run on the DGHSF agent worker can start a worker run.");
        }
        if (item.Status != WorkItemStatus.InProgress || item.IssueNumber is null)
        {
            throw new DomainRuleException(item.Status == WorkItemStatus.Accepted
                ? "Assign the agent first."
                : "Only an item that is in progress can start another worker run.");
        }
        return item;
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

        // The worker doesn't assign anyone on GitHub: its item is being worked on while it has a run that wasn't cancelled.
        var worker = item.Type.CanBeAssigned && item.Executor == AgentExecutor.DghsfWorker;
        var latestRun = worker ? await agentRuns.GetLatestAsync(repositoryFullName, id, cancellationToken) : null;
        var assignedToCopilot = worker ? latestRun is not null : GitHubApiClient.IsAssignedToCopilot(issue);
        var (progress, progressWarning) = !item.Type.CanBeAssigned
            ? (WorkItemProgressSnapshot.None, null)
            : worker
                ? await GetWorkerProgressAsync(repositoryFullName, number, issue.IsClosed, latestRun, cancellationToken)
                : await GetProgressAsync(repositoryFullName, number, issue.IsClosed, assignedToCopilot, cancellationToken);

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

    /// <summary>The worker's latest run and the linked pull requests (one GraphQL request); no Actions run to look up.</summary>
    private async Task<(WorkItemProgressSnapshot? Progress, string? Warning)> GetWorkerProgressAsync(string repositoryFullName, int issueNumber, bool issueClosed,
        AgentRun? run, CancellationToken cancellationToken)
    {
        try
        {
            var pullRequest = CopilotProgressEvaluator.SelectPullRequest(await gitHub.GetLinkedPullRequestsAsync(repositoryFullName, issueNumber, cancellationToken));
            return (CopilotProgressEvaluator.EvaluateWorker(issueClosed, run, pullRequest), null);
        }
        catch (GitHubApiException ex) when (!StopsEveryRequest(ex))
        {
            return (null, $"The agent's progress was kept as it was, because GitHub refused to show the linked pull requests: {ex.Message}");
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
    /// The issue body without the parent issues section and the marker. GitHub's web editor changes line endings and <see cref="IssueBody"/> trims, so
    /// the stored text is kept when only those differ; otherwise every refresh would count as a change.
    /// </summary>
    private static string DescriptionFromIssue(WorkItem item, GitHubIssue issue)
    {
        var body = ParentIssues().Replace(issue.Body ?? string.Empty, string.Empty);
        var description = Normalized(AnyMarker().Replace(body, string.Empty));
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

    /// <param name="ancestors">The items above <paramref name="item"/>, closest first; those with an issue are listed below the description.</param>
    public static string IssueBody(WorkItem item, IReadOnlyList<WorkItem>? ancestors = null)
    {
        return $"{item.Description.Trim()}\n\n{ParentIssuesSection(ancestors ?? [])}{Marker(item)}\n";
    }

    /// <summary>The heading above the list of parent issues at the bottom of the issue body.</summary>
    public const string ParentIssuesHeading = "### Parent issues";

    private const string ParentIssuesStart = "<!-- dghsf:parents -->";
    private const string ParentIssuesEnd = "<!-- dghsf:parents:end -->";

    /// <summary>
    /// The issues of every item above the item, from the top of the hierarchy down to the direct parent, so the
    /// Copilot cloud agent sees them as context. Between hidden markers, so a refresh leaves it out of the description.
    /// Empty when no parent has an issue.
    /// </summary>
    public static string ParentIssuesSection(IReadOnlyList<WorkItem> ancestors)
    {
        var lines = ancestors.Where(a => a.IssueNumber is not null).Reverse()
            .Select(a => $"- {a.Type.DisplayName}: {ParentIssueReference(a)}")
            .ToList();
        if (lines.Count == 0)
        {
            return string.Empty;
        }
        return $"{ParentIssuesStart}\n\n---\n\n{ParentIssuesHeading}\n\n{string.Join("\n", lines)}\n{ParentIssuesEnd}\n\n";
    }

    /// <summary>
    /// The bare reference (<c>#42</c>): GitHub renders it with the issue's state icon and current title. Don't add the
    /// title (GitHub then shows it twice) or make it a link with its own text (GitHub then drops the icon and title).
    /// </summary>
    private static string ParentIssueReference(WorkItem parent)
    {
        return $"#{parent.IssueNumber}";
    }

    /// <summary>The parent issues section, including the line breaks around it.</summary>
    [GeneratedRegex(@"\s*<!--\s*dghsf:parents\s*-->.*?<!--\s*dghsf:parents:end\s*-->", RegexOptions.Singleline)]
    private static partial Regex ParentIssues();

    /// <summary>The heading of the comment with the technical details.</summary>
    public const string TechnicalDetailsHeading = "## 🛠️ Technical details";

    /// <summary>The first comment of the issue: the item's technical details under a heading, with a hidden marker.</summary>
    public static string TechnicalDetailsComment(WorkItem item)
    {
        return $"""
            {TechnicalDetailsHeading}

            _Implementation notes for the agent, in addition to the description above._

            {item.TechnicalDetails.Trim()}

            {TechnicalDetailsMarker(item)}

            """;
    }

    /// <summary>Identifies the technical details comment, so a retried acceptance doesn't post it twice.</summary>
    public static string TechnicalDetailsMarker(WorkItem item)
    {
        return $"<!-- dghsf-technical-details:{item.Id} -->";
    }

    /// <summary>
    /// Posts the technical details (if any) as a comment, before Copilot is assigned: it reads the issue's comments too.
    /// On an issue that an earlier attempt created, the comment may already be there.
    /// </summary>
    private async Task PostTechnicalDetailsAsync(WorkItem item, GitHubIssue issue, bool reused, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.TechnicalDetails))
        {
            return;
        }
        if (reused)
        {
            var marker = TechnicalDetailsMarker(item);
            var comments = await gitHub.GetIssueCommentsAsync(item.RepositoryFullName, issue.Number, cancellationToken);
            if (comments.Any(c => c.Body?.Contains(marker, StringComparison.Ordinal) == true))
            {
                return;
            }
        }
        await gitHub.CreateIssueCommentAsync(item.RepositoryFullName, issue.Number, TechnicalDetailsComment(item), cancellationToken);
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
        if (item.Executor == AgentExecutor.DghsfWorker)
        {
            try
            {
                await agentRuns.CreateAsync(item.RepositoryFullName, item.Id, item.Model, agentRunOptions.Value.MaxAttempts, cancellationToken);
            }
            catch (DomainRuleException ex)
            {
                return new(item, ex.Message);
            }
            return new(await store.MarkAssignedAsync(item.RepositoryFullName, item.Id, cancellationToken), null);
        }

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
