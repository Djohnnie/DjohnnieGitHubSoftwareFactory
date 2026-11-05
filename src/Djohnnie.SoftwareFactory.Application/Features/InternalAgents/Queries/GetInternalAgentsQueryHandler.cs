using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class GetInternalAgentsQueryHandler(InternalAgentRegistry registry) : IRequestHandler<GetInternalAgentsQuery, Response<CollectionResult<InternalAgent>>>
{
    public async Task<Response<CollectionResult<InternalAgent>>> Handle(GetInternalAgentsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await registry.GetAllAsync(cancellationToken)));
    }
}
