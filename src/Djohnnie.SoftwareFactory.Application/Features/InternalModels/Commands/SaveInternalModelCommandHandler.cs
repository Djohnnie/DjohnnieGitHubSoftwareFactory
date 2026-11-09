using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

public sealed class SaveInternalModelCommandHandler(IInternalModelStore store) : IRequestHandler<SaveInternalModelCommand, Response<InternalModel>>
{
    public async Task<Response<InternalModel>> Handle(SaveInternalModelCommand request, CancellationToken cancellationToken)
    {
        return new(await store.SaveAsync(request.Dto, cancellationToken));
    }
}
