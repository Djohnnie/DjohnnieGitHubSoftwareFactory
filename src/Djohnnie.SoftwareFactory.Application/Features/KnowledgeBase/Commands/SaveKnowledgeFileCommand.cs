using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed record SaveKnowledgeFileCommand(SaveKnowledgeFileRequest Dto) : IRequest<Response<KnowledgeFile>>;
