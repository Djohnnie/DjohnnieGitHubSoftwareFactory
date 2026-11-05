using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public sealed record RemoveAuthSessionCommand(AuthSessionKeyRequest Dto) : IRequest<Response<OperationResult>>;
