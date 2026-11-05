using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AuthSessions;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public sealed class GetAuthSessionQueryHandler(IAuthSessionStore store) : IRequestHandler<GetAuthSessionQuery, Response<AuthSession?>>
{
    public async Task<Response<AuthSession?>> Handle(GetAuthSessionQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.Key, cancellationToken));
    }
}
