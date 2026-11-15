using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;

namespace Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;

/// <summary>The payload of an <see cref="Contracts.TodoTasks.TodoTaskKind.AnalyzeRepository"/> task.</summary>
public sealed record AnalyzeRepositoryPayload(RepositoryAnalysisMode Mode, string? PathPrefix, int MaxFeatures);

/// <summary>The payload of a <see cref="Contracts.TodoTasks.TodoTaskKind.WriteFeatureWorkItems"/> task.</summary>
/// <param name="FeatureId">The feature draft the analysis saved; the writer fills in its description and adds its children.</param>
/// <param name="OtherFeatures">The titles of the other features of the run.</param>
public sealed record WriteFeatureWorkItemsPayload(
    RepositoryAnalysisMode Mode,
    string? PathPrefix,
    Guid EpicId,
    Guid FeatureId,
    PlannedFeature Feature,
    IReadOnlyList<string> OtherFeatures);
