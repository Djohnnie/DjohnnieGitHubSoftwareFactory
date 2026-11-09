namespace Djohnnie.SoftwareFactory.Contracts.AiModels;

public sealed record SetModelEnabledRequest(Guid Id, bool Enabled);
public sealed record SetModelFavoriteRequest(Guid Id, bool IsFavorite);
public sealed record ImportModelsRequest(IReadOnlyList<AiModel> Models);
public sealed record ScrapeModelsRequest(string Url);
