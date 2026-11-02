namespace Djohnnie.SoftwareFactory.Contracts.AiModels;

public sealed record SetModelEnabledRequest(int Id, bool Enabled);
public sealed record ImportModelsRequest(IReadOnlyList<AiModel> Models);
public sealed record ScrapeModelsRequest(string Url);
