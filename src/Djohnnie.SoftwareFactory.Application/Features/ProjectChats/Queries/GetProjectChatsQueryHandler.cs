using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class GetProjectChatsQueryHandler(IProjectChatStore store) : IRequestHandler<GetProjectChatsQuery, Response<CollectionResult<ProjectChatSummary>>>
{
    public async Task<Response<CollectionResult<ProjectChatSummary>>> Handle(GetProjectChatsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(request.Dto.RepositoryFullName, cancellationToken)));
    }
}
