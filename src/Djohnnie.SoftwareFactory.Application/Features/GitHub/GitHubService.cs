using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Contracts.Agents;
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

    public async Task<List<AgentSummary>> GetCustomAgentsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetCustomAgentsQuery(new(repositoryFullName)), cancellationToken)).Dto.Items.ToList();
    }
}
