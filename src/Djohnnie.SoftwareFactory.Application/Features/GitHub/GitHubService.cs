using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GitHubService(ISender mediator)
{
    public async Task<GitHubRepository> GetRepositoryAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetRepositoryQuery(new(repositoryFullName)), cancellationToken)).Dto;
    }

    public async Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetRepositoriesQuery(new()), cancellationToken)).Dto.Items;
    }

    public async Task<IReadOnlyList<GitHubContentItem>> GetDirectoryAsync(string repositoryFullName, string path, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetDirectoryQuery(new(repositoryFullName, path)), cancellationToken)).Dto.Items;
    }

    public async Task<GitHubFile?> GetFileAsync(string repositoryFullName, string path, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetFileQuery(new(repositoryFullName, path)), cancellationToken)).Dto;
    }

    public async Task<GitHubCommitResult> PutFileAsync(string repositoryFullName, string path, string content, string message, string? sha, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new PutFileCommand(new(repositoryFullName, path, content, message, sha)), cancellationToken)).Dto;
    }

    public async Task<GitHubCommitResult> DeleteFileAsync(string repositoryFullName, string path, string message, string sha, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new DeleteFileCommand(new(repositoryFullName, path, message, sha)), cancellationToken)).Dto;
    }

    public async Task<CopilotCodeReviewSettings> GetCopilotCodeReviewSettingsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetCopilotCodeReviewSettingsQuery(new(repositoryFullName)), cancellationToken)).Dto;
    }

    public async Task<CopilotCodeReviewSettings> UpdateCopilotCodeReviewSettingsAsync(string repositoryFullName, CopilotCodeReviewSettingsUpdate changes, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new UpdateCopilotCodeReviewSettingsCommand(new(repositoryFullName, changes)), cancellationToken)).Dto;
    }

    /// <summary>The Copilot pull request activity of a repository owned by an organization, over the last 28 days.</summary>
    public async Task<CopilotRepositoryMetrics> GetCopilotRepositoryMetricsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetCopilotRepositoryMetricsQuery(new(repositoryFullName)), cancellationToken)).Dto;
    }

    /// <summary>The repository's open pull requests and the Copilot cloud agent sessions running in it right now.</summary>
    public async Task<CopilotPullRequestOverview> GetCopilotPullRequestOverviewAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetCopilotPullRequestOverviewQuery(new(repositoryFullName)), cancellationToken)).Dto;
    }

    /// <summary>The Copilot pull request activity of a personal repository over the last 28 days, worked out from its pull requests.</summary>
    public async Task<CopilotRepositoryMetrics> GetCopilotPullRequestActivityAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetCopilotPullRequestActivityQuery(new(repositoryFullName)), cancellationToken)).Dto;
    }

    /// <summary>This month's premium request or AI credit usage of the account that owns the repository.</summary>
    public async Task<CopilotBillingUsage> GetCopilotBillingUsageAsync(string repositoryFullName, bool ownedByOrganization, CopilotBillingReport report, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetCopilotBillingUsageQuery(new(repositoryFullName, ownedByOrganization, report)), cancellationToken)).Dto;
    }

    /// <summary>The repository's GitHub Actions workflows, by name.</summary>
    public async Task<IReadOnlyList<GitHubWorkflow>> GetWorkflowsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetWorkflowsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items;
    }

    /// <summary>One page of the repository's workflow runs (of one workflow, or of all), newest first.</summary>
    public async Task<PagedResult<GitHubWorkflowRun>> GetWorkflowRunsAsync(WorkflowRunsRequest request, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetWorkflowRunsQuery(request), cancellationToken)).Dto;
    }

    /// <summary>Every workflow of the repository with its latest run, most recently run first.</summary>
    public async Task<IReadOnlyList<LatestWorkflowRun>> GetLatestWorkflowRunsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetLatestWorkflowRunsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items;
    }

    public async Task<List<AgentSummary>> GetCustomAgentsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetCustomAgentsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items.ToList();
    }
}
