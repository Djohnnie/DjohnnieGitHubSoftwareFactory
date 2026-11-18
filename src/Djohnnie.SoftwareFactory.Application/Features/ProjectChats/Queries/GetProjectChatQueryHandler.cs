using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class GetProjectChatQueryHandler(IProjectChatStore store) : IRequestHandler<GetProjectChatQuery, Response<ProjectChat?>>
{
    public async Task<Response<ProjectChat?>> Handle(GetProjectChatQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
