using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

public sealed class GetRepositoryAgentRunsQueryHandler(IAgentRunStore store) : IRequestHandler<GetRepositoryAgentRunsQuery, Response<CollectionResult<AgentRunOverview>>>
{
    public const int MaxRuns = 25;

    public async Task<Response<CollectionResult<AgentRunOverview>>> Handle(GetRepositoryAgentRunsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetForRepositoryAsync(request.Dto.RepositoryFullName, MaxRuns, cancellationToken)));
    }
}
