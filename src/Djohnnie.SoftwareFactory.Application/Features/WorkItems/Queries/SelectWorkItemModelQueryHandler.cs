using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class SelectWorkItemModelQueryHandler(WorkItemModelSelector selector) : IRequestHandler<SelectWorkItemModelQuery, Response<ModelSelectionResult>>
{
    public async Task<Response<ModelSelectionResult>> Handle(SelectWorkItemModelQuery request, CancellationToken cancellationToken)
    {
        return new(new(await selector.TrySelectAsync(request.Dto.WorkItem, cancellationToken)));
    }
}
