using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.SyncLog;

public sealed record GetSyncLogRepositoriesQuery(EmptyRequest Dto) : IRequest<Response<CollectionResult<string>>>;
