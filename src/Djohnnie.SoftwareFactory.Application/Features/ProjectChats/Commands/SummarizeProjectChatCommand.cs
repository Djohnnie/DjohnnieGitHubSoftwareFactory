using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

/// <summary>
/// Lets the Chat Summarizer summarize the conversation once enough chat messages aren't summarized; <c>true</c> when it
/// stored a new summary.
/// </summary>
public sealed record SummarizeProjectChatCommand(RepositoryItemRequest Dto) : IRequest<Response<ProjectChatSummarizeResult>>;
