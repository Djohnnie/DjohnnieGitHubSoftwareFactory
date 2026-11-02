using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class EpicTemplateHandlers(EpicTemplateStore store) :
    IRequestHandler<GetEpicTemplatesQuery, Response<CollectionResult<EpicTemplate>>>,
    IRequestHandler<GetEpicTemplateQuery, Response<EpicTemplate?>>,
    IRequestHandler<SaveEpicTemplateCommand, Response<EpicTemplate>>,
    IRequestHandler<DeleteEpicTemplateCommand, Response<OperationResult>>
{
    public async Task<Response<CollectionResult<EpicTemplate>>> Handle(GetEpicTemplatesQuery request, CancellationToken cancellationToken) =>
        new(new(await store.GetAllAsync(cancellationToken)));

    public async Task<Response<EpicTemplate?>> Handle(GetEpicTemplateQuery request, CancellationToken cancellationToken) =>
        new(await store.GetAsync(request.Dto.Id, cancellationToken));

    public async Task<Response<EpicTemplate>> Handle(SaveEpicTemplateCommand request, CancellationToken cancellationToken) =>
        new(await store.SaveAsync(request.Dto.Template, cancellationToken));

    public async Task<Response<OperationResult>> Handle(DeleteEpicTemplateCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }
}
