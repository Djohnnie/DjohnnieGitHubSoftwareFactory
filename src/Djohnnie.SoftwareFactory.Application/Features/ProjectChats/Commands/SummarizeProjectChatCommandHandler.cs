using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

/// <summary>
/// Every <see cref="ProjectChat.SummaryInterval"/> chat messages: the Chat Summarizer merges the last summary with the
/// messages after it. Without a model, with the agent disabled or when it fails, nothing is summarized and the model keeps
/// getting the messages; a later exchange tries again.
/// </summary>
public sealed class SummarizeProjectChatCommandHandler(
    IProjectChatStore store,
    InternalAgentRegistry registry,
    BackgroundAgents agents,
    ILogger<SummarizeProjectChatCommandHandler> logger) : IRequestHandler<SummarizeProjectChatCommand, Response<ProjectChatSummarizeResult>>
{
    public async Task<Response<ProjectChatSummarizeResult>> Handle(SummarizeProjectChatCommand request, CancellationToken cancellationToken)
    {
        var (repositoryFullName, id) = (request.Dto.RepositoryFullName, request.Dto.Id);
        if (!agents.IsConfigured)
        {
            return new(new(false));
        }

        try
        {
            var history = await store.GetHistoryToSummarizeAsync(repositoryFullName, id, ProjectChat.SummaryInterval, cancellationToken);
            if (history is null)
            {
                return new(new(false));
            }

            var agent = await registry.GetAsync(InternalAgentKeys.ChatSummarizer, cancellationToken);
            if (!agent.IsEnabled)
            {
                return new(new(false));
            }

            var summary = await agents.SummarizeChatAsync(agent, history.PreviousSummary, history.Messages, cancellationToken);
            if (summary is null)
            {
                return new(new(false));
            }

            await store.SaveSummaryAsync(repositoryFullName, id, summary, AssistantConversation.SummaryPayload(summary), history.UpToSequence, cancellationToken);
            return new(new(true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The Chat Summarizer could not summarize conversation {Id} of {Repository}.", id, repositoryFullName);
            return new(new(false));
        }
    }
}
