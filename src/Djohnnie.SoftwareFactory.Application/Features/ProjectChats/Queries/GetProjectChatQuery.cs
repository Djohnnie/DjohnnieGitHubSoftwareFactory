using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed record GetProjectChatQuery(RepositoryItemRequest Dto) : IRequest<Response<ProjectChat?>>;
