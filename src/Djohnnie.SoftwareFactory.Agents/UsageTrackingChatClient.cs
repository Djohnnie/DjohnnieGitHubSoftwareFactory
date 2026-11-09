using System.Runtime.CompilerServices;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Reports the tokens of every model request. The agent's function invocation loop sits on top of this client, so each
/// round trip of a tool-using turn is reported on its own.
/// </summary>
internal sealed class UsageTrackingChatClient(IChatClient innerClient, Func<ModelUsage, Task> onUsage) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        await ReportAsync(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? usage = null;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                foreach (var content in update.Contents.OfType<UsageContent>())
                {
                    usage ??= new UsageDetails();
                    usage.Add(content.Details);
                }

                yield return update;
            }
        }
        finally
        {
            // Also when the turn is stopped: tokens the service already reported have been paid for.
            await ReportAsync(usage);
        }
    }

    private async Task ReportAsync(UsageDetails? usage)
    {
        var input = usage?.InputTokenCount ?? 0;
        var output = usage?.OutputTokenCount ?? 0;
        if (input > 0 || output > 0)
        {
            await onUsage(new ModelUsage(input, output, usage?.CachedInputTokenCount ?? 0, usage?.ReasoningTokenCount ?? 0));
        }
    }
}
