using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class ImportModelsCommandHandler(IAiModelStore store) : IRequestHandler<ImportModelsCommand, Response<ModelImportResult>>
{
    public async Task<Response<ModelImportResult>> Handle(ImportModelsCommand request, CancellationToken cancellationToken)
    {
        return new(await store.ImportAsync(request.Dto.Models, cancellationToken));
    }
}
