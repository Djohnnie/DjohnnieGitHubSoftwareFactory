using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// Lists the open pull requests and Copilot's running and queued Actions runs (three requests or more), matches the runs to
/// the pull requests by branch and the pull requests to work items by the pull request their progress is about. Not cached:
/// it is about what is going on right now.
/// </summary>
public sealed class GetCopilotPullRequestOverviewQueryHandler(GitHubApiClient gitHub, IWorkItemStore workItems, TimeProvider time)
    : IRequestHandler<GetCopilotPullRequestOverviewQuery, Response<CopilotPullRequestOverview>>
{
    public async Task<Response<CopilotPullRequestOverview>> Handle(GetCopilotPullRequestOverviewQuery request, CancellationToken cancellationToken)
    {
        var repository = request.Dto.RepositoryFullName;
        var pullRequests = await gitHub.GetOpenPullRequestsAsync(repository, cancellationToken);
        var runs = await gitHub.GetActiveCopilotRunsAsync(repository, cancellationToken);
        var items = await workItems.GetAllAsync(repository, cancellationToken: cancellationToken);

        // The newest session per branch: a branch normally has one at a time.
        var sessions = (runs ?? [])
            .Select(r => new CopilotSession(r.Id, r.HtmlUrl, string.Equals(r.Status, "queued", StringComparison.OrdinalIgnoreCase),
                r.RunStartedAt ?? r.CreatedAt, r.HeadBranch))
            .OrderByDescending(s => s.StartedAt)
            .ToList();
        var byBranch = sessions.Where(s => s.Branch is not null).GroupBy(s => s.Branch!).ToDictionary(g => g.Key, g => g.First());
        var itemsByPullRequest = items.Where(i => i.PullRequestNumber is not null)
            .GroupBy(i => i.PullRequestNumber!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.UpdatedAt).First());

        var open = pullRequests.Select(pr =>
        {
            var session = pr.Head?.Ref is { } branch ? byBranch.GetValueOrDefault(branch) : null;
            var item = itemsByPullRequest.GetValueOrDefault(pr.Number);
            return new OpenPullRequest(pr.Number, pr.Title ?? $"#{pr.Number}", pr.HtmlUrl ?? $"https://github.com/{repository}/pull/{pr.Number}",
                pr.User?.Login, pr.User?.IsCopilot == true, pr.Draft, pr.Head?.Ref, pr.CreatedAt, pr.UpdatedAt, session,
                item is null ? null : new OpenPullRequestWorkItem(item.Id, item.Type, item.Title));
        }).ToList();

        var branchesWithPullRequest = open.Select(p => p.Branch).OfType<string>().ToHashSet();
        var others = sessions.Where(s => s.Branch is null || !branchesWithPullRequest.Contains(s.Branch)).ToList();

        return new(new CopilotPullRequestOverview(open, others, runs is null, time.GetUtcNow()));
    }
}
