using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class RepositoryHandlers(RepositoryAccessStore store) :
    IRequestHandler<GetRecentRepositoriesQuery, Response<CollectionResult<RecentRepository>>>,
    IRequestHandler<RecordRepositoryAccessCommand, Response<OperationResult>>
{
    public async Task<Response<CollectionResult<RecentRepository>>> Handle(GetRecentRepositoriesQuery request, CancellationToken cancellationToken) =>
        new(new(await store.GetRecentAsync(cancellationToken)));

    public async Task<Response<OperationResult>> Handle(RecordRepositoryAccessCommand request, CancellationToken cancellationToken)
    {
        await store.RecordAccessAsync(request.Dto.RepositoryFullName, cancellationToken);
        return new(new());
    }
}
