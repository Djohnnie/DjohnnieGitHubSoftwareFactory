using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class GetAllInternalAgentUsageQueryHandler(IInternalAgentStore store) : IRequestHandler<GetAllInternalAgentUsageQuery, Response<CollectionResult<InternalAgentUsage>>>
{
    public async Task<Response<CollectionResult<InternalAgentUsage>>> Handle(GetAllInternalAgentUsageQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetRecentUsageAsync(null, InternalAgentUsage.RecentOverallCount, cancellationToken)));
    }
}
