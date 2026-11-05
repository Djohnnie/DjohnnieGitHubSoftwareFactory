using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed record ImportModelsCommand(ImportModelsRequest Dto) : IRequest<Response<ModelImportResult>>;
