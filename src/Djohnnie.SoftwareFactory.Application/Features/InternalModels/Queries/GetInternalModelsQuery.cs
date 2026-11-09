using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

public sealed record GetInternalModelsQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<InternalModel>>>;
