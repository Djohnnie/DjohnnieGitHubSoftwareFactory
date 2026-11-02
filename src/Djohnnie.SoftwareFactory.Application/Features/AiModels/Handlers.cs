using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class ModelHandlers(AiModelStore store, ModelPricingScraper scraper) :
    IRequestHandler<GetModelsQuery, Response<CollectionResult<AiModel>>>,
    IRequestHandler<GetEnabledModelsQuery, Response<CollectionResult<AiModel>>>,
    IRequestHandler<SaveModelCommand, Response<AiModel>>,
    IRequestHandler<SetModelEnabledCommand, Response<OperationResult>>,
    IRequestHandler<DeleteModelCommand, Response<OperationResult>>,
    IRequestHandler<ImportModelsCommand, Response<ModelImportResult>>,
    IRequestHandler<ScrapeModelsQuery, Response<CollectionResult<AiModel>>>
{
    public async Task<Response<CollectionResult<AiModel>>> Handle(GetModelsQuery request, CancellationToken cancellationToken) =>
        new(new(await store.GetAllAsync(cancellationToken)));

    public async Task<Response<CollectionResult<AiModel>>> Handle(GetEnabledModelsQuery request, CancellationToken cancellationToken) =>
        new(new(await store.GetEnabledAsync(cancellationToken)));

    public async Task<Response<AiModel>> Handle(SaveModelCommand request, CancellationToken cancellationToken) =>
        new(await store.SaveAsync(request.Dto, cancellationToken));

    public async Task<Response<OperationResult>> Handle(SetModelEnabledCommand request, CancellationToken cancellationToken)
    {
        await store.SetEnabledAsync(request.Dto.Id, request.Dto.Enabled, cancellationToken);
        return new(new());
    }

    public async Task<Response<OperationResult>> Handle(DeleteModelCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }

    public async Task<Response<ModelImportResult>> Handle(ImportModelsCommand request, CancellationToken cancellationToken) =>
        new(await store.ImportAsync(request.Dto.Models, cancellationToken));

    public async Task<Response<CollectionResult<AiModel>>> Handle(ScrapeModelsQuery request, CancellationToken cancellationToken) =>
        new(new(await scraper.ScrapeAsync(request.Dto.Url, cancellationToken)));
}
