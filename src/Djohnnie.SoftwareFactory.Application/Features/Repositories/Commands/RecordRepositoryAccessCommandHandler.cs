using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class RecordRepositoryAccessCommandHandler(IRepositoryStore store) : IRequestHandler<RecordRepositoryAccessCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(RecordRepositoryAccessCommand request, CancellationToken cancellationToken)
    {
        await store.RecordAccessAsync(request.Dto.RepositoryFullName, cancellationToken);
        return new(new());
    }
}
