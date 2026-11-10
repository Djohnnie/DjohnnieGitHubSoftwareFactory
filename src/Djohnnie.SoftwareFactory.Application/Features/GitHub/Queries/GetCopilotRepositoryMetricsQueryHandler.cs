using System.Net;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Api = Djohnnie.SoftwareFactory.Contracts.GitHub.Api;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// Reads one <c>repos-1-day</c> report per day (GitHub has no multi-day repository report) and keeps the selected
/// repository's row. A day's report covers every repository of the organization, so it is cached per organization and day:
/// switching repositories or returning to the overview doesn't download the reports again.
/// </summary>
public sealed class GetCopilotRepositoryMetricsQueryHandler(GitHubApiClient gitHub, IMemoryCache cache, TimeProvider time)
    : IRequestHandler<GetCopilotRepositoryMetricsQuery, Response<CopilotRepositoryMetrics>>
{
    public const int Days = 28;

    /// <summary>GitHub can take this many days to publish a report; a missing report that recent isn't an error.</summary>
    public const int PublishDelayDays = 3;

    private const int ParallelDownloads = 4;
    private static readonly TimeSpan PublishedReportLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan PendingReportLifetime = TimeSpan.FromMinutes(30);

    public async Task<Response<CopilotRepositoryMetrics>> Handle(GetCopilotRepositoryMetricsQuery request, CancellationToken cancellationToken)
    {
        var repository = request.Dto.RepositoryFullName;
        var organization = repository.Split('/')[0];
        var (from, to) = Period(time);

        using var throttle = new SemaphoreSlim(ParallelDownloads);
        var reports = await Task.WhenAll(Enumerable.Range(0, Days).Select(async offset =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                return await GetDayAsync(organization, from.AddDays(offset), to, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        }));

        var days = reports
            .Select(report => report.GetValueOrDefault(repository))
            .OfType<CopilotRepositoryDay>()
            .OrderBy(d => d.Day)
            .ToList();
        return new(new CopilotRepositoryMetrics(from, to, days));
    }

    /// <summary>The last <see cref="Days"/> whole days in UTC, up to yesterday: today's report never exists yet.</summary>
    public static (DateOnly From, DateOnly To) Period(TimeProvider time)
    {
        var to = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-1);
        return (to.AddDays(-(Days - 1)), to);
    }

    private async Task<IReadOnlyDictionary<string, CopilotRepositoryDay>> GetDayAsync(string organization, DateOnly day, DateOnly latest, CancellationToken cancellationToken)
    {
        var key = $"copilot-repos-1-day:{organization.ToLowerInvariant()}:{day:yyyy-MM-dd}";
        if (cache.TryGetValue(key, out IReadOnlyDictionary<string, CopilotRepositoryDay>? cached) && cached is not null)
        {
            return cached;
        }

        var pending = day > latest.AddDays(-PublishDelayDays);
        IReadOnlyList<Api.RepositoryMetricsRow>? rows;
        try
        {
            rows = await gitHub.GetCopilotRepositoryMetricsReportAsync(organization, day, cancellationToken);
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && pending)
        {
            rows = null;
        }

        IReadOnlyDictionary<string, CopilotRepositoryDay> report = (rows ?? [])
            .Where(r => !string.IsNullOrEmpty(r.OwnerName) && !string.IsNullOrEmpty(r.Name))
            .GroupBy(r => $"{r.OwnerName}/{r.Name}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => ToDay(day, g.First()), StringComparer.OrdinalIgnoreCase);
        cache.Set(key, report, rows is null && pending ? PendingReportLifetime : PublishedReportLifetime);
        return report;
    }

    private static CopilotRepositoryDay ToDay(DateOnly day, Api.RepositoryMetricsRow row)
    {
        var pullRequests = row.PullRequests;
        var review = row.CodeReview;
        return new CopilotRepositoryDay(
            day,
            pullRequests?.TotalCreated ?? 0,
            pullRequests?.TotalCreatedByCopilot ?? 0,
            pullRequests?.TotalMerged ?? 0,
            pullRequests?.TotalMergedCreatedByCopilot ?? 0,
            pullRequests?.TotalReviewedByCopilot ?? 0,
            review?.TotalCommentsEvaluated ?? 0,
            review?.TotalCommentsAddressed ?? 0,
            (review?.BySeverity ?? [])
                .Where(s => !string.IsNullOrEmpty(s.Severity))
                .Select(s => new CopilotReviewCommentCount(s.Severity!, s.TotalCommentsEvaluated, s.TotalCommentsAddressed))
                .ToList());
    }
}
