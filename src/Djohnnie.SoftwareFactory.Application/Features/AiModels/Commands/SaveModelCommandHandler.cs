using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class SaveModelCommandHandler(IAiModelStore store) : IRequestHandler<SaveModelCommand, Response<AiModel>>
{
    public async Task<Response<AiModel>> Handle(SaveModelCommand request, CancellationToken cancellationToken)
    {
        return new(await store.SaveAsync(request.Dto, cancellationToken));
    }
}
