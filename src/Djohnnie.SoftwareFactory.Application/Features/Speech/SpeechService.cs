using Djohnnie.SoftwareFactory.Contracts.Speech;
using MediatR;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.Speech;

public sealed class SpeechService(ISender mediator, IOptions<SpeechOptions> options)
{
    /// <summary>Whether an Azure AI Speech resource is configured; otherwise dictation is not offered.</summary>
    public bool IsEnabled => options.Value.IsConfigured;

    /// <summary>A short-lived token the browser uses to recognize speech directly with Azure AI Speech.</summary>
    public async Task<SpeechToken> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetSpeechTokenQuery(new()), cancellationToken)).Dto;
    }

    /// <summary>
    /// Lets the Description Writer merge a dictated text into a description (following its template); nothing is stored.
    /// Falls back to adding the transcript at the end when the agent is unavailable.
    /// </summary>
    public async Task<DictatedDescriptionResult> WriteDescriptionAsync(WriteDictatedDescriptionRequest request, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new WriteDictatedDescriptionQuery(request), cancellationToken)).Dto;
    }
}
