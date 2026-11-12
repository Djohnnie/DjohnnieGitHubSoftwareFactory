using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>
/// Every workflow of the repository with its latest run, most recently run first and the workflows that never ran last.
/// One request per workflow (a few at a time), because the latest runs of the whole repository can all belong to one busy workflow.
/// </summary>
public sealed class GetLatestWorkflowRunsQueryHandler(GitHubApiClient gitHub) : IRequestHandler<GetLatestWorkflowRunsQuery, Response<CollectionResult<LatestWorkflowRun>>>
{
    private const int ParallelRequests = 4;

    public async Task<Response<CollectionResult<LatestWorkflowRun>>> Handle(GetLatestWorkflowRunsQuery request, CancellationToken cancellationToken)
    {
        var repository = request.Dto.RepositoryFullName;
        var workflows = await gitHub.GetWorkflowsAsync(repository, cancellationToken);

        using var throttle = new SemaphoreSlim(ParallelRequests);
        var latest = await Task.WhenAll(workflows.Select(async workflow =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var runs = await gitHub.GetWorkflowRunsAsync(repository, workflow.Id, 0, 1, cancellationToken);
                return new LatestWorkflowRun(workflow, runs.Items.FirstOrDefault());
            }
            finally
            {
                throttle.Release();
            }
        }));

        return new(new(latest
            .OrderBy(l => l.LatestRun is null)
            .ThenByDescending(l => l.LatestRun?.CreatedAt)
            .ThenBy(l => l.Workflow.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()));
    }
}
