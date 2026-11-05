using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed record SetRepositoryFavoriteCommand(SetRepositoryFavoriteRequest Dto) : IRequest<Response<OperationResult>>;
