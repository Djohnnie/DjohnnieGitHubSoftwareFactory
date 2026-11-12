using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetWorkflowsQueryHandler(GitHubApiClient gitHub) : IRequestHandler<GetWorkflowsQuery, Response<CollectionResult<GitHubWorkflow>>>
{
    public async Task<Response<CollectionResult<GitHubWorkflow>>> Handle(GetWorkflowsQuery request, CancellationToken cancellationToken)
    {
        var workflows = await gitHub.GetWorkflowsAsync(request.Dto.RepositoryFullName, cancellationToken);
        return new(new(workflows.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList()));
    }
}
