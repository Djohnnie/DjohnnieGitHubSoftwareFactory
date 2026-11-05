using Djohnnie.SoftwareFactory.Contracts.AiModels;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class AiModelService(ISender mediator)
{
    public async Task<List<AiModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetModelsQuery(new()), cancellationToken)).Dto.Items.ToList();

    public async Task<List<AiModel>> GetEnabledAsync(CancellationToken cancellationToken = default) =>
        (await mediator.Send(new GetEnabledModelsQuery(new()), cancellationToken)).Dto.Items.ToList();

    public async Task<AiModel> SaveAsync(AiModel model, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new SaveModelCommand(model), cancellationToken)).Dto;

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken cancellationToken = default) =>
        await mediator.Send(new SetModelEnabledCommand(new(id, enabled)), cancellationToken);

    public async Task SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default) =>
        await mediator.Send(new SetModelFavoriteCommand(new(id, isFavorite)), cancellationToken);

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        await mediator.Send(new DeleteModelCommand(new(id)), cancellationToken);

    public async Task<ModelImportResult> ImportAsync(IEnumerable<AiModel> models, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new ImportModelsCommand(new(models.ToList())), cancellationToken)).Dto;

    public async Task<IReadOnlyList<AiModel>> ScrapeAsync(string url, CancellationToken cancellationToken = default) =>
        (await mediator.Send(new ScrapeModelsQuery(new(url)), cancellationToken)).Dto.Items;
}
