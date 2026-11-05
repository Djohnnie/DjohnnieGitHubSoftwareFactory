using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed record GetEpicsQuery(RepositoryRequest Dto) : IRequest<Response<CollectionResult<Epic>>>;
