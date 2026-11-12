using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed record GetWorkflowRunsQuery(WorkflowRunsRequest Dto) : IRequest<Response<PagedResult<GitHubWorkflowRun>>>;
