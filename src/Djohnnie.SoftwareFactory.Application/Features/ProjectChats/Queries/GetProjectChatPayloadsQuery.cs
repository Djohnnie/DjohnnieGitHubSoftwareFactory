using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed record GetProjectChatPayloadsQuery(RepositoryItemRequest Dto) : IRequest<Response<CollectionResult<string>>>;
