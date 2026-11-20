using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class FixMergeConflictsCommandHandler(MergeConflictFixes fixes)
    : IRequestHandler<FixMergeConflictsCommand, Response<MergeConflictFixResult>>
{
    public async Task<Response<MergeConflictFixResult>> Handle(FixMergeConflictsCommand request, CancellationToken cancellationToken)
    {
        return new(await fixes.RequestWhenConflictingAsync(request.Dto.RepositoryFullName, request.Dto.Id, cancellationToken));
    }
}
