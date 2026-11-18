using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class StartProjectChatCommandHandler(IProjectChatStore store) : IRequestHandler<StartProjectChatCommand, Response<ProjectChatSummary>>
{
    public async Task<Response<ProjectChatSummary>> Handle(StartProjectChatCommand request, CancellationToken cancellationToken)
    {
        return new(await store.StartAsync(request.Dto.RepositoryFullName, request.Dto.Title, cancellationToken));
    }
}
