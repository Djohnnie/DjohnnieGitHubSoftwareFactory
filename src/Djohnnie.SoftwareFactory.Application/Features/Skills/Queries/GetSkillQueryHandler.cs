using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class GetSkillQueryHandler(ISkillStore store) : IRequestHandler<GetSkillQuery, Response<Skill?>>
{
    public async Task<Response<Skill?>> Handle(GetSkillQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.Id, cancellationToken));
    }
}
