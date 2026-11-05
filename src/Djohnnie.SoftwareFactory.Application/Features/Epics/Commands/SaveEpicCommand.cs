using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed record SaveEpicCommand(Epic Dto) : IRequest<Response<Epic>>;
