using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class AcceptEpicCommandHandler(EpicWorkflow workflow) : IRequestHandler<AcceptEpicCommand, Response<EpicAcceptResult>>
{
    public async Task<Response<EpicAcceptResult>> Handle(AcceptEpicCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.AcceptAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
