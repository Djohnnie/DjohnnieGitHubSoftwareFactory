using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class RenameProjectChatCommandHandler(IProjectChatStore store) : IRequestHandler<RenameProjectChatCommand, Response<ProjectChatSummary>>
{
    public async Task<Response<ProjectChatSummary>> Handle(RenameProjectChatCommand request, CancellationToken cancellationToken)
    {
        return new(await store.RenameAsync(request.Dto.RepositoryFullName, request.Dto.Id, request.Dto.Title, cancellationToken));
    }
}
