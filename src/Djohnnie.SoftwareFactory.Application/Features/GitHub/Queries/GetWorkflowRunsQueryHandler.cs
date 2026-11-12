using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetWorkflowRunsQueryHandler(GitHubApiClient gitHub) : IRequestHandler<GetWorkflowRunsQuery, Response<PagedResult<GitHubWorkflowRun>>>
{
    public async Task<Response<PagedResult<GitHubWorkflowRun>>> Handle(GetWorkflowRunsQuery request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        return new(await gitHub.GetWorkflowRunsAsync(dto.RepositoryFullName, dto.WorkflowId, dto.Page, dto.PageSize, cancellationToken));
    }
}
