using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Speech;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Speech;

/// <summary>Lets the Description Writer merge a dictated text into a description; nothing is stored.</summary>
public sealed record WriteDictatedDescriptionQuery(WriteDictatedDescriptionRequest Dto) : IRequest<Response<DictatedDescriptionResult>>;
