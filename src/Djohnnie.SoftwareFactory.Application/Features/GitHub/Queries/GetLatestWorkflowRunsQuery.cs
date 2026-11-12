using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed record GetLatestWorkflowRunsQuery(RepositoryRequest Dto) : IRequest<Response<CollectionResult<LatestWorkflowRun>>>;
