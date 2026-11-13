using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class DisableSkillCommandHandler(SkillWorkflow workflow) : IRequestHandler<DisableSkillCommand, Response<SkillCommitResult>>
{
    public async Task<Response<SkillCommitResult>> Handle(DisableSkillCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.DisableAsync(request.Dto.RepositoryFullName, request.Dto.SkillId, cancellationToken));
    }
}
