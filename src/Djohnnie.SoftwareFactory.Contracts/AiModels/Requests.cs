namespace Djohnnie.SoftwareFactory.Contracts.AiModels;

public sealed record SetModelEnabledRequest(int Id, bool Enabled);
public sealed record SetModelFavoriteRequest(int Id, bool IsFavorite);
public sealed record ImportModelsRequest(IReadOnlyList<AiModel> Models);
public sealed record ScrapeModelsRequest(string Url);
