using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed record GetEpicQuery(RepositoryItemRequest Dto) : IRequest<Response<Epic?>>;
