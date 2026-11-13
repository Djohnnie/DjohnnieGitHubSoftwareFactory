using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class GetMindmapsQueryHandler(IMindmapStore store) : IRequestHandler<GetMindmapsQuery, Response<CollectionResult<MindmapSummary>>>
{
    public async Task<Response<CollectionResult<MindmapSummary>>> Handle(GetMindmapsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(request.Dto.RepositoryFullName, cancellationToken)));
    }
}
