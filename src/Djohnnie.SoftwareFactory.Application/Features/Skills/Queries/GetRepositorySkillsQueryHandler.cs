using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class GetRepositorySkillsQueryHandler(SkillWorkflow workflow) : IRequestHandler<GetRepositorySkillsQuery, Response<RepositorySkills>>
{
    public async Task<Response<RepositorySkills>> Handle(GetRepositorySkillsQuery request, CancellationToken cancellationToken)
    {
        return new(await workflow.GetRepositorySkillsAsync(request.Dto.RepositoryFullName, cancellationToken));
    }
}
