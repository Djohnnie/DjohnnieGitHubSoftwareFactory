using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// Lists the open pull requests, Copilot's running and queued Actions runs (three requests or more) and the DGHSF agent
/// worker's tasks, matches the sessions to the pull requests by branch (and a worker task also by its pull request or its
/// work item's), and the pull requests to work items by the pull request their progress is about. Not cached: it is about
/// what is going on right now.
/// </summary>
public sealed class GetCopilotPullRequestOverviewQueryHandler(GitHubApiClient gitHub, IWorkItemStore workItems, IAgentRunStore agentRuns, TimeProvider time)
    : IRequestHandler<GetCopilotPullRequestOverviewQuery, Response<CopilotPullRequestOverview>>
{
    /// <summary>The newest worker tasks that are read: enough to hold the active ones and the ones behind the open pull requests.</summary>
    private const int MaxWorkerTasks = 200;

    public async Task<Response<CopilotPullRequestOverview>> Handle(GetCopilotPullRequestOverviewQuery request, CancellationToken cancellationToken)
    {
        var repository = request.Dto.RepositoryFullName;
        var pullRequests = await gitHub.GetOpenPullRequestsAsync(repository, cancellationToken);
        var runs = await gitHub.GetActiveCopilotRunsAsync(repository, cancellationToken);
        var items = await workItems.GetAllAsync(repository, cancellationToken: cancellationToken);
        var tasks = await agentRuns.GetForRepositoryAsync(repository, MaxWorkerTasks, cancellationToken);

        // The newest session per branch: a branch normally has one at a time.
        var sessions = (runs ?? [])
            .Select(r => new CopilotSession(r.Id, r.HtmlUrl, string.Equals(r.Status, "queued", StringComparison.OrdinalIgnoreCase),
                r.RunStartedAt ?? r.CreatedAt, r.HeadBranch))
            .OrderByDescending(s => s.StartedAt)
            .ToList();
        var byBranch = sessions.Where(s => s.Branch is not null).GroupBy(s => s.Branch!).ToDictionary(g => g.Key, g => g.First());
        var itemsById = items.ToDictionary(i => i.Id);
        var itemsByPullRequest = items.Where(i => i.PullRequestNumber is not null)
            .GroupBy(i => i.PullRequestNumber!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.UpdatedAt).First());

        // The pull requests the worker opened, and its active tasks (one per work item at a time).
        var workerPullRequests = tasks.Select(t => t.Run.PullRequestNumber).OfType<int>().ToHashSet();
        var workerSessions = tasks.Where(t => t.Run.IsActive)
            .Select(t =>
            {
                var item = itemsById.GetValueOrDefault(t.Run.WorkItemId);
                var session = new AgentWorkerSession(t.Run.Id, t.Run.Status != AgentRunStatus.Running, StartedAt(t), t.Run.Branch,
                    item is null ? null : Reference(item));
                return (Session: session, PullRequest: t.Run.PullRequestNumber ?? item?.PullRequestNumber);
            })
            .ToList();

        var open = pullRequests.Select(pr =>
        {
            var branch = pr.Head?.Ref;
            var session = branch is not null ? byBranch.GetValueOrDefault(branch) : null;
            var workerSession = workerSessions.FirstOrDefault(w => w.PullRequest == pr.Number || (branch is not null && w.Session.Branch == branch)).Session;
            var item = itemsByPullRequest.GetValueOrDefault(pr.Number);
            var byCopilot = pr.User?.IsCopilot == true;
            AgentExecutor? agent = workerSession is not null || workerPullRequests.Contains(pr.Number) ? AgentExecutor.DghsfWorker
                : byCopilot || session is not null ? AgentExecutor.CopilotCloudAgent
                : item?.Executor;
            return new OpenPullRequest(pr.Number, pr.Title ?? $"#{pr.Number}", pr.HtmlUrl ?? $"https://github.com/{repository}/pull/{pr.Number}",
                pr.User?.Login, byCopilot, pr.Draft, branch, pr.CreatedAt, pr.UpdatedAt, agent, session, workerSession,
                item is null ? null : Reference(item));
        }).ToList();

        var branchesWithPullRequest = open.Select(p => p.Branch).OfType<string>().ToHashSet();
        var others = sessions.Where(s => s.Branch is null || !branchesWithPullRequest.Contains(s.Branch)).ToList();
        var matchedWorkerSessions = open.Select(p => p.WorkerSession).OfType<AgentWorkerSession>().Select(s => s.RunId).ToHashSet();
        var otherWorkerSessions = workerSessions.Select(w => w.Session).Where(s => !matchedWorkerSessions.Contains(s.RunId)).ToList();

        return new(new CopilotPullRequestOverview(open, others, otherWorkerSessions, runs is null, time.GetUtcNow()));
    }

    private static OpenPullRequestWorkItem Reference(WorkItem item)
    {
        return new OpenPullRequestWorkItem(item.Id, item.Type, item.Title, item.Progress, item.PullRequestChecks);
    }

    /// <summary>When the running task's current run started, or else when the task was queued or created.</summary>
    private static DateTimeOffset StartedAt(AgentRunOverview task)
    {
        if (task.Run.Status == AgentRunStatus.Running && task.Runs.FirstOrDefault(a => !a.Status.IsFinal) is { } current)
        {
            return current.StartedAt;
        }
        return task.Run.QueuedAt ?? task.Run.CreatedAt;
    }
}
