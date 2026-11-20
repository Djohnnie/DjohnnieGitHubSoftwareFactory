using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

public sealed class GetWorkItemAgentTasksQueryHandler(IAgentRunStore store) : IRequestHandler<GetWorkItemAgentTasksQuery, Response<CollectionResult<AgentRunOverview>>>
{
    public async Task<Response<CollectionResult<AgentRunOverview>>> Handle(GetWorkItemAgentTasksQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetOverviewsForWorkItemAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken)));
    }
}
