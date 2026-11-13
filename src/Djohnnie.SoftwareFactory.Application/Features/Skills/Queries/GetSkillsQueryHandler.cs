using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class GetSkillsQueryHandler(ISkillStore store) : IRequestHandler<GetSkillsQuery, Response<CollectionResult<Skill>>>
{
    public async Task<Response<CollectionResult<Skill>>> Handle(GetSkillsQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(cancellationToken)));
    }
}
