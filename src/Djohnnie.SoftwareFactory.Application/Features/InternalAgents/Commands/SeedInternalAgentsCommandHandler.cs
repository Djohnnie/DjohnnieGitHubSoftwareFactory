using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

public sealed class SeedInternalAgentsCommandHandler(InternalAgentRegistry registry) : IRequestHandler<SeedInternalAgentsCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(SeedInternalAgentsCommand request, CancellationToken cancellationToken)
    {
        await registry.EnsureSeededAsync(cancellationToken);
        return new(new());
    }
}
