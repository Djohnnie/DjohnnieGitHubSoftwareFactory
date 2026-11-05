using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed record GetModelsQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<AiModel>>>;
public sealed record GetEnabledModelsQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<AiModel>>>;
public sealed record SaveModelCommand(AiModel Dto) : IRequest<Response<AiModel>>;
public sealed record SetModelEnabledCommand(SetModelEnabledRequest Dto) : IRequest<Response<OperationResult>>;
public sealed record SetModelFavoriteCommand(SetModelFavoriteRequest Dto) : IRequest<Response<OperationResult>>;
public sealed record DeleteModelCommand(EntityIdRequest Dto) : IRequest<Response<OperationResult>>;
public sealed record ImportModelsCommand(ImportModelsRequest Dto) : IRequest<Response<ModelImportResult>>;
public sealed record ScrapeModelsQuery(ScrapeModelsRequest Dto) : IRequest<Response<CollectionResult<AiModel>>>;
