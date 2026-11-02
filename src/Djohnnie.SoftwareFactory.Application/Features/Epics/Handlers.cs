using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class EpicHandlers(EpicStore store, EpicWorkflow workflow) :
    IRequestHandler<GetEpicsQuery, Response<CollectionResult<Epic>>>,
    IRequestHandler<GetEpicQuery, Response<Epic?>>,
    IRequestHandler<SaveEpicCommand, Response<Epic>>,
    IRequestHandler<DeleteEpicCommand, Response<OperationResult>>,
    IRequestHandler<AcceptEpicCommand, Response<EpicAcceptResult>>,
    IRequestHandler<AssignEpicAgentCommand, Response<EpicAcceptResult>>
{
    public async Task<Response<CollectionResult<Epic>>> Handle(GetEpicsQuery request, CancellationToken cancellationToken) =>
        new(new(await store.GetAllAsync(request.Dto.RepositoryFullName, cancellationToken)));

    public async Task<Response<Epic?>> Handle(GetEpicQuery request, CancellationToken cancellationToken) =>
        new(await store.GetAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));

    public async Task<Response<Epic>> Handle(SaveEpicCommand request, CancellationToken cancellationToken) =>
        new(await store.SaveAsync(request.Dto, cancellationToken));

    public async Task<Response<OperationResult>> Handle(DeleteEpicCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken);
        return new(new());
    }

    public async Task<Response<EpicAcceptResult>> Handle(AcceptEpicCommand request, CancellationToken cancellationToken) =>
        new(await workflow.AcceptAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));

    public async Task<Response<EpicAcceptResult>> Handle(AssignEpicAgentCommand request, CancellationToken cancellationToken) =>
        new(await workflow.AssignAgentAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
}
