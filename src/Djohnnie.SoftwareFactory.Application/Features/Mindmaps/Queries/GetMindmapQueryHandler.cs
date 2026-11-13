using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class GetMindmapQueryHandler(IMindmapStore store) : IRequestHandler<GetMindmapQuery, Response<Mindmap?>>
{
    public async Task<Response<Mindmap?>> Handle(GetMindmapQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
