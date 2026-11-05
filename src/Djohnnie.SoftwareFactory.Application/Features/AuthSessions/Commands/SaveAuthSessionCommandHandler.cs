using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public sealed class SaveAuthSessionCommandHandler(IAuthSessionStore store) : IRequestHandler<SaveAuthSessionCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(SaveAuthSessionCommand request, CancellationToken cancellationToken)
    {
        await store.SaveAsync(request.Dto, cancellationToken);
        return new(new());
    }
}
