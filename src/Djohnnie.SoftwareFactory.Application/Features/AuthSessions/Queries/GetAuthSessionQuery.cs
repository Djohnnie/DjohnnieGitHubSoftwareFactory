using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public sealed record GetAuthSessionQuery(AuthSessionKeyRequest Dto) : IRequest<Response<AuthSession?>>;
