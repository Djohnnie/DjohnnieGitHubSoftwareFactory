using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class SaveSkillCommandHandler(SkillWorkflow workflow) : IRequestHandler<SaveSkillCommand, Response<Skill>>
{
    public async Task<Response<Skill>> Handle(SaveSkillCommand request, CancellationToken cancellationToken)
    {
        return new(await workflow.SaveAsync(request.Dto.Skill, cancellationToken));
    }
}
