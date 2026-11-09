using Djohnnie.SoftwareFactory.Contracts.InternalModels;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

/// <summary>Manages the catalog of Microsoft Foundry models the internal agents can run on.</summary>
public sealed class InternalModelService(ISender mediator)
{
    public async Task<List<InternalModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetInternalModelsQuery(new()), cancellationToken)).Dto.Items.ToList();
    }

    public async Task<InternalModel> SaveAsync(InternalModel model, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveInternalModelCommand(model), cancellationToken)).Dto;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteInternalModelCommand(new(id)), cancellationToken);
    }
}
