using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

public sealed record SaveInternalModelCommand(InternalModel Dto) : IRequest<Response<InternalModel>>;
