using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Speech;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Speech;

public sealed record GetSpeechTokenQuery(EmptyRequest Dto) : IRequest<Response<SpeechToken>>;
