using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed record GetRecentRepositoriesQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<RepositorySummary>>>;
public sealed record GetFavoriteRepositoriesQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<RepositorySummary>>>;
public sealed record RecordRepositoryAccessCommand(RepositoryRequest Dto) : IRequest<Response<OperationResult>>;
public sealed record SetRepositoryFavoriteCommand(SetRepositoryFavoriteRequest Dto) : IRequest<Response<OperationResult>>;
