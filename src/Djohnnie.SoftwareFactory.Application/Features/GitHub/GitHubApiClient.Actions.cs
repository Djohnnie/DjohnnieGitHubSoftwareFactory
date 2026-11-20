using System.Net;
using System.Net.Http.Json;
using Api = Djohnnie.SoftwareFactory.Contracts.GitHub.Api;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>GitHub Actions: workflows, their runs, and the runs of Copilot's agent sessions.</summary>
public sealed partial class GitHubApiClient
{
    /// <summary>
    /// Copilot's latest agent session on <paramref name="branch"/>: the newest Actions run that GitHub started for the
    /// Copilot cloud agent (a <c>dynamic</c> run) rather than for a workflow file of the repository.
    /// </summary>
    /// <returns>
    /// No run when there is none or the token may not read Actions; the latter only hides the run, nothing else, and is
    /// told by <see cref="CopilotRunLookup.CanReadActions"/>.
    /// </returns>
    public async Task<CopilotRunLookup> GetLatestCopilotRunAsync(string repositoryFullName, string branch, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"repos/{repositoryFullName}/actions/runs?branch={Uri.EscapeDataString(branch)}&per_page=30", null, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden && !await IsRateLimitedAsync(response, cancellationToken))
        {
            return new(null, CanReadActions: false);
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var runs = (await response.Content.ReadFromJsonAsync<Api.WorkflowRunsResponse>(cancellationToken))?.WorkflowRuns ?? [];
        return new(runs.Where(IsCopilotRun).MaxBy(r => r.CreatedAt), CanReadActions: true);
    }

    /// <summary>The repository's GitHub Actions workflows (all pages), including GitHub's own dynamic ones such as Copilot's agent.</summary>
    public async Task<IReadOnlyList<GitHubWorkflow>> GetWorkflowsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var workflows = new List<GitHubWorkflow>();
        string? url = $"repos/{repositoryFullName}/actions/workflows?per_page={PageSize}";
        while (url is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            workflows.AddRange((await response.Content.ReadFromJsonAsync<Api.WorkflowsResponse>(cancellationToken))?.Workflows ?? []);
            url = GetNextLink(response);
        }
        return workflows;
    }

    /// <summary>One page of the repository's workflow runs (of one workflow, or of all), newest first, with the number of runs on all pages.</summary>
    /// <param name="page">Zero-based.</param>
    public async Task<PagedResult<GitHubWorkflowRun>> GetWorkflowRunsAsync(string repositoryFullName, long? workflowId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var runs = workflowId is { } id ? $"actions/workflows/{id}/runs" : "actions/runs";
        using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/{runs}?per_page={pageSize}&page={page + 1}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<Api.WorkflowRunsResponse>(cancellationToken);
        return new(result?.WorkflowRuns ?? [], result?.TotalCount ?? 0);
    }

    /// <summary>
    /// Copilot's agent sessions that are running or waiting to run in the repository: its in-progress and queued
    /// Actions runs (one request each).
    /// </summary>
    /// <returns><c>null</c> when the token may not read Actions.</returns>
    public async Task<IReadOnlyList<GitHubWorkflowRun>?> GetActiveCopilotRunsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var runs = new List<GitHubWorkflowRun>();
        foreach (var status in new[] { "in_progress", "queued" })
        {
            using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/actions/runs?status={status}&per_page={PageSize}", null, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden && !await IsRateLimitedAsync(response, cancellationToken))
            {
                return null;
            }
            await EnsureSuccessAsync(response, cancellationToken);
            runs.AddRange((await response.Content.ReadFromJsonAsync<Api.WorkflowRunsResponse>(cancellationToken))?.WorkflowRuns ?? []);
        }
        // A run can move from queued to in progress between the two requests.
        return runs.Where(IsCopilotRun).DistinctBy(r => r.Id).ToList();
    }

    /// <summary>
    /// An agent session of the Copilot cloud agent (<c>dynamic/copilot-swe-agent/copilot</c>). Copilot code review runs as a
    /// dynamic run on the pull request's branch too (<c>dynamic/agents/copilot-pull-request-reviewer</c>, "Running Copilot Code
    /// Review"); that one is no agent session, or a review would look like Copilot working on (and finishing) a revision.
    /// </summary>
    public static bool IsCopilotRun(GitHubWorkflowRun run)
    {
        if (!string.Equals(run.Event, "dynamic", StringComparison.OrdinalIgnoreCase)
            || run.Path?.Contains("copilot-pull-request-reviewer", StringComparison.OrdinalIgnoreCase) == true
            || run.Name?.Contains("code review", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }
        return run.Path?.Contains("copilot", StringComparison.OrdinalIgnoreCase) == true || run.Name?.Contains("copilot", StringComparison.OrdinalIgnoreCase) == true;
    }
}
