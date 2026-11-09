using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class GetInternalAgentUsageQueryHandler(IInternalAgentStore store) : IRequestHandler<GetInternalAgentUsageQuery, Response<CollectionResult<InternalAgentUsage>>>
{
    public async Task<Response<CollectionResult<InternalAgentUsage>>> Handle(GetInternalAgentUsageQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetRecentUsageAsync(request.Dto.Key, InternalAgentUsage.RecentCount, cancellationToken)));
    }
}
