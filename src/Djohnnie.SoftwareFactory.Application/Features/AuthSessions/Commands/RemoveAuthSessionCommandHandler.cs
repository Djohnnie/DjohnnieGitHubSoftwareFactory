using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public sealed class RemoveAuthSessionCommandHandler(IAuthSessionStore store) : IRequestHandler<RemoveAuthSessionCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(RemoveAuthSessionCommand request, CancellationToken cancellationToken)
    {
        await store.RemoveAsync(request.Dto.Key, cancellationToken);
        return new(new());
    }
}
