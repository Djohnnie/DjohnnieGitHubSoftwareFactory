using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.SyncLog;

public sealed record GetSyncLogQuery(SyncLogQuery Dto) : IRequest<Response<PagedResult<SyncLogEvent>>>;
