using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.Speech;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.Speech;

/// <summary>
/// Lets the Description Writer merge the dictated text into the description. When it is disabled, not configured or
/// fails, the transcript is added as-is at the end, so nothing that was said gets lost.
/// </summary>
public sealed class WriteDictatedDescriptionQueryHandler(
    InternalAgentRegistry registry,
    BackgroundAgents agents,
    ILogger<WriteDictatedDescriptionQueryHandler> logger) : IRequestHandler<WriteDictatedDescriptionQuery, Response<DictatedDescriptionResult>>
{
    public async Task<Response<DictatedDescriptionResult>> Handle(WriteDictatedDescriptionQuery request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        if (agents.IsConfigured)
        {
            try
            {
                var agent = await registry.GetAsync(InternalAgentKeys.DescriptionWriter, cancellationToken);
                if (agent.IsEnabled && await agents.WriteDescriptionAsync(agent, dto, cancellationToken) is { } written)
                {
                    return new(new DictatedDescriptionResult(written, true));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The Description Writer could not merge a dictated {Subject} description.", dto.Subject);
            }
        }

        return new(new DictatedDescriptionResult(Append(dto.Description, dto.Transcript), false));
    }

    /// <summary>The transcript as a new paragraph after the description.</summary>
    public static string Append(string? description, string transcript)
    {
        var current = (description ?? string.Empty).TrimEnd();
        return current.Length == 0 ? transcript.Trim() : $"{current}\n\n{transcript.Trim()}";
    }
}
