using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class GetLinkedMindmapIdeasQueryHandler(IMindmapStore store)
    : IRequestHandler<GetLinkedMindmapIdeasQuery, Response<CollectionResult<MindmapIdeaLink>>>
{
    public async Task<Response<CollectionResult<MindmapIdeaLink>>> Handle(GetLinkedMindmapIdeasQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetLinkedIdeasAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken)));
    }
}
