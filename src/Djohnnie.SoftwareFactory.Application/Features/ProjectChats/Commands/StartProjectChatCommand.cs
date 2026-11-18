using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed record StartProjectChatCommand(StartProjectChatRequest Dto) : IRequest<Response<ProjectChatSummary>>;
