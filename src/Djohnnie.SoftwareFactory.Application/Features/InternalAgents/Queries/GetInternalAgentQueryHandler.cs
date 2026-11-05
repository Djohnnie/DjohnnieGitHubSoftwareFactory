using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class GetInternalAgentQueryHandler(InternalAgentRegistry registry) : IRequestHandler<GetInternalAgentQuery, Response<InternalAgent>>
{
    public async Task<Response<InternalAgent>> Handle(GetInternalAgentQuery request, CancellationToken cancellationToken)
    {
        return new(await registry.GetAsync(request.Dto.Key, cancellationToken));
    }
}
