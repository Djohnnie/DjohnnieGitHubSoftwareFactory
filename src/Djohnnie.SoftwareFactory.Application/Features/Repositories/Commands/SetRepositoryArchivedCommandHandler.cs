using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class SetRepositoryArchivedCommandHandler(IRepositoryStore store) : IRequestHandler<SetRepositoryArchivedCommand, Response<SetRepositoryArchivedResult>>
{
    public async Task<Response<SetRepositoryArchivedResult>> Handle(SetRepositoryArchivedCommand request, CancellationToken cancellationToken)
    {
        return new(new(await store.SetArchivedAsync(request.Dto.RepositoryFullName, request.Dto.IsArchived, cancellationToken)));
    }
}
