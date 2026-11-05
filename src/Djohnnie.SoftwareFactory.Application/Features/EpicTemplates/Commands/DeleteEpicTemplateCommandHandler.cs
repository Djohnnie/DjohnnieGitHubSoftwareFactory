using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class DeleteEpicTemplateCommandHandler(IEpicTemplateStore store) : IRequestHandler<DeleteEpicTemplateCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteEpicTemplateCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }
}
