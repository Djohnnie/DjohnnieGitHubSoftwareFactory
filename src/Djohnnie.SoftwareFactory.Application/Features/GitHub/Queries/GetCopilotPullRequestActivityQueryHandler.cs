using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// Lists the pull requests updated in the period (that includes every one created or merged in it) and reads the reviews of
/// each to find Copilot code review. A day counts in UTC, like GitHub's usage metrics reports. The result is cached briefly
/// per repository, because it takes a request per pull request.
/// </summary>
public sealed class GetCopilotPullRequestActivityQueryHandler(GitHubApiClient gitHub, IMemoryCache cache, TimeProvider time)
    : IRequestHandler<GetCopilotPullRequestActivityQuery, Response<CopilotRepositoryMetrics>>
{
    private const int ParallelRequests = 4;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<Response<CopilotRepositoryMetrics>> Handle(GetCopilotPullRequestActivityQuery request, CancellationToken cancellationToken)
    {
        var repository = request.Dto.RepositoryFullName;
        var (from, to) = GetCopilotRepositoryMetricsQueryHandler.Period(time);
        var key = $"copilot-pull-request-activity:{repository.ToLowerInvariant()}:{from:yyyy-MM-dd}";
        if (cache.TryGetValue(key, out CopilotRepositoryMetrics? cached) && cached is not null)
        {
            return new(cached);
        }

        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var pullRequests = await gitHub.GetPullRequestsUpdatedSinceAsync(repository, since, cancellationToken);

        using var throttle = new SemaphoreSlim(ParallelRequests);
        var firstCopilotReviews = await Task.WhenAll(pullRequests.Select(async pullRequest =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var reviews = await gitHub.GetPullRequestReviewsAsync(repository, pullRequest.Number, cancellationToken);
                return reviews
                    .Where(r => r.User?.IsCopilot == true && r.SubmittedAt is not null)
                    .Select(r => (DateOnly?)Day(r.SubmittedAt!.Value))
                    .Min();
            }
            finally
            {
                throttle.Release();
            }
        }));

        var days = new Dictionary<DateOnly, Counts>();
        Counts On(DateOnly day)
        {
            if (!days.TryGetValue(day, out var counts))
            {
                days[day] = counts = new Counts();
            }
            return counts;
        }
        bool InPeriod(DateOnly day)
        {
            return day >= from && day <= to;
        }

        for (var i = 0; i < pullRequests.Count; i++)
        {
            var pullRequest = pullRequests[i];
            var byCopilot = pullRequest.User?.IsCopilot == true;
            if (Day(pullRequest.CreatedAt) is var created && InPeriod(created))
            {
                var counts = On(created);
                counts.Created++;
                counts.CreatedByCopilot += byCopilot ? 1 : 0;
            }
            if (pullRequest.MergedAt is { } mergedAt && Day(mergedAt) is var merged && InPeriod(merged))
            {
                var counts = On(merged);
                counts.Merged++;
                counts.MergedByCopilot += byCopilot ? 1 : 0;
            }
            if (firstCopilotReviews[i] is { } reviewed && InPeriod(reviewed))
            {
                On(reviewed).ReviewedByCopilot++;
            }
        }

        var metrics = new CopilotRepositoryMetrics(from, to,
            days.OrderBy(d => d.Key)
                .Select(d => new CopilotRepositoryDay(d.Key, d.Value.Created, d.Value.CreatedByCopilot, d.Value.Merged,
                    d.Value.MergedByCopilot, d.Value.ReviewedByCopilot, 0, 0, []))
                .ToList(),
            FromPullRequests: true);
        cache.Set(key, metrics, Lifetime);
        return new(metrics);
    }

    private static DateOnly Day(DateTimeOffset moment)
    {
        return DateOnly.FromDateTime(moment.UtcDateTime);
    }

    private sealed class Counts
    {
        public int Created { get; set; }
        public int CreatedByCopilot { get; set; }
        public int Merged { get; set; }
        public int MergedByCopilot { get; set; }
        public int ReviewedByCopilot { get; set; }
    }
}
