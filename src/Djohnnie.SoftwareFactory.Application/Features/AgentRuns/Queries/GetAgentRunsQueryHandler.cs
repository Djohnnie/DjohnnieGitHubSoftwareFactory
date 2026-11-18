using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

public sealed class GetAgentRunsQueryHandler(IAgentRunStore store) : IRequestHandler<GetAgentRunsQuery, Response<CollectionResult<AgentRun>>>
{
    public async Task<Response<CollectionResult<AgentRun>>> Handle(GetAgentRunsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetForWorkItemAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken)));
    }
}
