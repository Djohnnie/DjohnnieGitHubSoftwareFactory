using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

/// <summary>The signed-in sessions behind the session cookies, stored in SQL so sign-ins survive restarts and deployments.</summary>
public sealed class AuthSessionService(ISender mediator)
{
    public async Task<AuthSession?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetAuthSessionQuery(new(key)), cancellationToken)).Dto;
    }

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new SaveAuthSessionCommand(session), cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new RemoveAuthSessionCommand(new(key)), cancellationToken);
    }
}
