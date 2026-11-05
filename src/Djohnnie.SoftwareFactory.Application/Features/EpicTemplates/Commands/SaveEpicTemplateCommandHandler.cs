using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.EpicTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class SaveEpicTemplateCommandHandler(IEpicTemplateStore store) : IRequestHandler<SaveEpicTemplateCommand, Response<EpicTemplate>>
{
    public async Task<Response<EpicTemplate>> Handle(SaveEpicTemplateCommand request, CancellationToken cancellationToken)
    {
        return new(await store.SaveAsync(request.Dto.Template, cancellationToken));
    }
}
