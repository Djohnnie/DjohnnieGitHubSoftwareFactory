using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class DeleteSkillCommandHandler(ISkillStore store) : IRequestHandler<DeleteSkillCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteSkillCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }
}
