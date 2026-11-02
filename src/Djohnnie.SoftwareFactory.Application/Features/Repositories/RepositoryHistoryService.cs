using Djohnnie.SoftwareFactory.Contracts.Repositories;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class RepositoryHistoryService(ISender mediator)
{
    public async Task<IReadOnlyList<RecentRepository>> GetRecentAsync(CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetRecentRepositoriesQuery(new()), cancellationToken)).Dto.Items;

    public async Task RecordAccessAsync(string fullName, CancellationToken cancellationToken = default) =>
        await mediator.Send(new RecordRepositoryAccessCommand(new(fullName)), cancellationToken);
}
