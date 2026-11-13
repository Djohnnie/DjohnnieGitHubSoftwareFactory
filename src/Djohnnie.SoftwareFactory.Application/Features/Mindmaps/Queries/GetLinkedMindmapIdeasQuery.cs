using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

/// <summary>The mindmap ideas linked to a work item (the request's <c>Id</c>).</summary>
public sealed record GetLinkedMindmapIdeasQuery(RepositoryItemRequest Dto) : IRequest<Response<CollectionResult<MindmapIdeaLink>>>;
