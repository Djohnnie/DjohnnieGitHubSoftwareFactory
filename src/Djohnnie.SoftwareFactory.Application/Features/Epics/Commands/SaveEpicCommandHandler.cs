using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class SaveEpicCommandHandler(IEpicStore store) : IRequestHandler<SaveEpicCommand, Response<Epic>>
{
    public async Task<Response<Epic>> Handle(SaveEpicCommand request, CancellationToken cancellationToken)
    {
        return new(await store.SaveAsync(request.Dto, cancellationToken));
    }
}
