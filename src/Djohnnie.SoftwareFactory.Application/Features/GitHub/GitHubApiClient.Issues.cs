using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Api = Djohnnie.SoftwareFactory.Contracts.GitHub.Api;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>Labels, issues, sub-issues and assigning the Copilot cloud agent.</summary>
public sealed partial class GitHubApiClient
{
    /// <summary>Creates the label when the repository doesn't have it yet.</summary>
    public async Task EnsureLabelAsync(string repositoryFullName, string name, string color, string? description, CancellationToken cancellationToken = default)
    {
        using (var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/labels/{Uri.EscapeDataString(name)}", null, cancellationToken))
        {
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(response, cancellationToken);
                return;
            }
        }

        using var create = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/labels",
            JsonContent.Create(new Api.LabelRequest(name, color, description)), cancellationToken);
        // 422: created concurrently in the meantime.
        if (create.StatusCode != HttpStatusCode.UnprocessableEntity)
        {
            await EnsureSuccessAsync(create, cancellationToken);
        }
    }

    public async Task<GitHubIssue> CreateIssueAsync(string repositoryFullName, string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/issues",
            JsonContent.Create(new Api.CreateIssueRequest(title, body, labels)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubIssue>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>
    /// Finds the most recent issue with <paramref name="label"/> whose body contains <paramref name="marker"/> among the
    /// issues created or updated since <paramref name="since"/>, or <c>null</c>. Uses the issues list (immediately
    /// consistent, unlike search); the time filter keeps it short however many issues the label has.
    /// </summary>
    public async Task<GitHubIssue?> FindIssueByMarkerAsync(string repositoryFullName, string label, string marker, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        string? url = $"repos/{repositoryFullName}/issues?labels={Uri.EscapeDataString(label)}&state=all&sort=created&direction=desc&per_page={PageSize}"
            + $"&since={Uri.EscapeDataString(since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}";
        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var issues = await response.Content.ReadFromJsonAsync<List<GitHubIssue>>(cancellationToken) ?? [];
            if (issues.FirstOrDefault(i => i.Body?.Contains(marker, StringComparison.Ordinal) == true) is { } found)
            {
                return found;
            }
            url = GetNextLink(response);
        }

        return null;
    }

    public async Task<GitHubIssue> GetIssueAsync(string repositoryFullName, int issueNumber, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/issues/{issueNumber}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubIssue>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    /// <summary>Makes an issue a sub-issue of <paramref name="parentIssueNumber"/>.</summary>
    /// <param name="subIssueId">GitHub's id of the child issue (not its number).</param>
    public async Task AddSubIssueAsync(string repositoryFullName, int parentIssueNumber, long subIssueId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/issues/{parentIssueNumber}/sub_issues",
            JsonContent.Create(new Api.AddSubIssueRequest(subIssueId)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Assigns the Copilot cloud agent to an issue, optionally with a custom agent and model.
    /// GitHub silently ignores assignees it can't assign, so check <see cref="IsAssignedToCopilot"/> on the result.
    /// </summary>
    public async Task<GitHubIssue> AssignCopilotAsync(string repositoryFullName, int issueNumber, CopilotAssignment assignment, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"repos/{repositoryFullName}/issues/{issueNumber}/assignees",
            JsonContent.Create(new Api.AssignRequest([CopilotLogin], assignment)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubIssue>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
    }

    public const string CopilotLogin = "copilot-swe-agent[bot]";

    public static bool IsAssignedToCopilot(GitHubIssue issue)
    {
        return issue.Assignees?.Any(a => a.Login.Contains("copilot", StringComparison.OrdinalIgnoreCase)) == true;
    }
}
