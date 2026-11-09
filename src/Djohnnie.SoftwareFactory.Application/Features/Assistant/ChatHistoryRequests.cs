using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Assistant;

public sealed record GetChatDisplayMessagesQuery(ChatOwnerRequest Dto) : IRequest<Response<CollectionResult<ChatDisplayMessage>>>;

public sealed class GetChatDisplayMessagesQueryHandler(IChatConversationStore store)
    : IRequestHandler<GetChatDisplayMessagesQuery, Response<CollectionResult<ChatDisplayMessage>>>
{
    public async Task<Response<CollectionResult<ChatDisplayMessage>>> Handle(GetChatDisplayMessagesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetDisplayMessagesAsync(request.Dto.Owner, cancellationToken)));
    }
}

public sealed record GetChatPayloadsQuery(ChatOwnerRequest Dto) : IRequest<Response<CollectionResult<string>>>;

public sealed class GetChatPayloadsQueryHandler(IChatConversationStore store)
    : IRequestHandler<GetChatPayloadsQuery, Response<CollectionResult<string>>>
{
    public async Task<Response<CollectionResult<string>>> Handle(GetChatPayloadsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetPayloadsAsync(request.Dto.Owner, cancellationToken)));
    }
}

public sealed record AppendChatMessagesCommand(AppendChatMessagesRequest Dto) : IRequest<Response<OperationResult>>;

public sealed class AppendChatMessagesCommandHandler(IChatConversationStore store)
    : IRequestHandler<AppendChatMessagesCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(AppendChatMessagesCommand request, CancellationToken cancellationToken)
    {
        await store.AppendAsync(request.Dto.Owner, request.Dto.CopilotKey, request.Dto.Messages, cancellationToken);
        return new(new());
    }
}

public sealed record CloseChatConversationCommand(ChatOwnerRequest Dto) : IRequest<Response<OperationResult>>;

public sealed class CloseChatConversationCommandHandler(IChatConversationStore store)
    : IRequestHandler<CloseChatConversationCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(CloseChatConversationCommand request, CancellationToken cancellationToken)
    {
        await store.CloseAsync(request.Dto.Owner, cancellationToken);
        return new(new());
    }
}
