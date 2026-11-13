using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class EnableSkillCommandHandler(SkillWorkflow workflow) : IRequestHandler<EnableSkillCommand, Response<SkillCommitResult>>
{
    public async Task<Response<SkillCommitResult>> Handle(EnableSkillCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.EnableAsync(request.Dto.RepositoryFullName, request.Dto.SkillId, cancellationToken));
    }
}
