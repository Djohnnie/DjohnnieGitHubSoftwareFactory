using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class AppendProjectChatMessagesCommandHandler(IProjectChatStore store) : IRequestHandler<AppendProjectChatMessagesCommand, Response<ProjectChatSummary>>
{
    public async Task<Response<ProjectChatSummary>> Handle(AppendProjectChatMessagesCommand request, CancellationToken cancellationToken)
    {
        return new(await store.AppendAsync(request.Dto.RepositoryFullName, request.Dto.Id, request.Dto.Messages, cancellationToken));
    }
}
