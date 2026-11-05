using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class SaveWorkItemTemplateCommandHandler(IWorkItemTemplateStore store) : IRequestHandler<SaveWorkItemTemplateCommand, Response<WorkItemTemplate>>
{
    public async Task<Response<WorkItemTemplate>> Handle(SaveWorkItemTemplateCommand request, CancellationToken cancellationToken)
    {
        return new(await store.SaveAsync(request.Dto.Template, cancellationToken));
    }
}
