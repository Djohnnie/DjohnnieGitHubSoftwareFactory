using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api = Djohnnie.SoftwareFactory.Contracts.GitHub.Api;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>Copilot settings and usage: code review settings, usage metrics reports and billing usage.</summary>
public sealed partial class GitHubApiClient
{
    public async Task<CopilotCodeReviewSettings> GetCopilotCodeReviewSettingsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, CopilotCodeReviewSettingsUrl(repositoryFullName), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CopilotCodeReviewSettings>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>Changes only the settings set in <paramref name="changes"/> and returns all settings as they are now.</summary>
    public async Task<CopilotCodeReviewSettings> UpdateCopilotCodeReviewSettingsAsync(string repositoryFullName, CopilotCodeReviewSettingsUpdate changes, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Patch, CopilotCodeReviewSettingsUrl(repositoryFullName), JsonContent.Create(changes), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CopilotCodeReviewSettings>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    private static string CopilotCodeReviewSettingsUrl(string repositoryFullName)
    {
        return $"repos/{repositoryFullName}/copilot/code-review/settings";
    }

    /// <summary>The REST API version the Copilot usage metrics and billing usage endpoints are documented for.</summary>
    public const string UsageApiVersion = "2026-03-10";

    /// <summary>
    /// Gets the organization's <c>repos-1-day</c> Copilot usage metrics report: one row per repository with pull request
    /// activity that day. <c>null</c> when GitHub has no report for the day (204).
    /// </summary>
    public async Task<IReadOnlyList<Api.RepositoryMetricsRow>?> GetCopilotRepositoryMetricsReportAsync(string organization, DateOnly day, CancellationToken cancellationToken = default)
    {
        var url = $"orgs/{Uri.EscapeDataString(organization)}/copilot/metrics/reports/repos-1-day?day={day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken, apiVersion: UsageApiVersion);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var report = await response.Content.ReadFromJsonAsync<Api.MetricsReportResponse>(cancellationToken);
        var rows = new List<Api.RepositoryMetricsRow>();
        foreach (var link in report?.DownloadLinks ?? [])
        {
            rows.AddRange(await DownloadNdjsonAsync<Api.RepositoryMetricsRow>(link, cancellationToken));
        }
        return rows;
    }

    /// <summary>Downloads a report file from a signed link, without the GitHub token: the link points outside GitHub's API.</summary>
    private async Task<List<T>> DownloadNdjsonAsync<T>(string link, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new GitHubApiException(HttpStatusCode.BadGateway, "GitHub returned an invalid report download link.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubApiException(response.StatusCode, $"Downloading the Copilot metrics report failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var rows = new List<T>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line) && JsonSerializer.Deserialize<T>(line) is { } row)
            {
                rows.Add(row);
            }
        }
        return rows;
    }

    /// <summary>
    /// Gets the premium request or AI credit usage of an organization or user for one month. For a user, it only covers the
    /// Copilot plans the user bought personally.
    /// </summary>
    public async Task<IReadOnlyList<CopilotBillingUsageItem>> GetCopilotBillingUsageAsync(string account, bool organization, CopilotBillingReport report, int year, int month, CancellationToken cancellationToken = default)
    {
        var owner = organization ? $"organizations/{Uri.EscapeDataString(account)}" : $"users/{Uri.EscapeDataString(account)}";
        var path = report switch
        {
            CopilotBillingReport.PremiumRequests => "premium_request",
            CopilotBillingReport.AiCredits => "ai_credit",
            _ => throw new ArgumentOutOfRangeException(nameof(report), report, null)
        };
        using var response = await SendAsync(HttpMethod.Get, $"{owner}/settings/billing/{path}/usage?year={year}&month={month}", null, cancellationToken, apiVersion: UsageApiVersion);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<Api.BillingUsageResponse>(cancellationToken))?.UsageItems ?? [];
    }
}
