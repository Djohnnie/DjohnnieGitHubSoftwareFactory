using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class GetProjectChatPayloadsQueryHandler(IProjectChatStore store) : IRequestHandler<GetProjectChatPayloadsQuery, Response<CollectionResult<string>>>
{
    public async Task<Response<CollectionResult<string>>> Handle(GetProjectChatPayloadsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetPayloadsAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken)));
    }
}
