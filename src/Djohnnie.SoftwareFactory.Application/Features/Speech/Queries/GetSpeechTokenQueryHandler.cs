using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Speech;
using MediatR;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.Speech;

public sealed class GetSpeechTokenQueryHandler(ISpeechTokenIssuer issuer, IOptions<SpeechOptions> options) : IRequestHandler<GetSpeechTokenQuery, Response<SpeechToken>>
{
    public async Task<Response<SpeechToken>> Handle(GetSpeechTokenQuery request, CancellationToken cancellationToken)
    {
        var issued = await issuer.IssueAsync(cancellationToken);
        var speech = options.Value;
        return new(new SpeechToken(issued.Token, speech.Region!.Trim(), speech.Languages, issued.ExpiresAt));
    }
}
