using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>What the Repository Analyst gets to plan the epics and features of a repository.</summary>
/// <param name="Outline">The filtered file tree as an indented outline with file sizes.</param>
/// <param name="Backlog">Every work item of the repository (<see cref="WorkItemBacklog.Outline"/>), so nothing is planned twice.</param>
/// <param name="ExistingFeatures">The features the repository already has; planned features with their titles are dropped.</param>
public sealed record RepositoryAnalysisInput(
    string RepositoryFullName,
    RepositoryAnalysisMode Mode,
    string? PathPrefix,
    int MaxFeatures,
    string Outline,
    string Backlog,
    IReadOnlyList<WorkItem> ExistingFeatures,
    WorkItemTemplate? EpicTemplate);

/// <summary>An epic the Repository Analyst planned, with its features.</summary>
public sealed record PlannedEpic(string Title, string Description, IReadOnlyList<PlannedFeature> Features);

/// <summary>A feature the Repository Analyst planned; the Repository Work Item Writer describes it later.</summary>
/// <param name="Paths">The files and folders the feature is about, relative to the repository root.</param>
public sealed record PlannedFeature(string Title, string Summary, IReadOnlyList<string> Paths);

/// <summary>What the Repository Work Item Writer gets to describe one feature and write its user stories.</summary>
/// <param name="OtherFeatures">The titles of the other features of the run, which are out of scope for this one.</param>
/// <param name="ExistingChildTitles">The children the feature already has, so they aren't written again.</param>
/// <param name="Backlog">Every work item of the repository (<see cref="WorkItemBacklog.Outline"/>), so nothing is written twice.</param>
public sealed record FeatureWritingInput(
    string RepositoryFullName,
    RepositoryAnalysisMode Mode,
    string EpicTitle,
    string EpicDescription,
    PlannedFeature Feature,
    string Outline,
    string Backlog,
    IReadOnlyList<string> OtherFeatures,
    IReadOnlyList<string> ExistingChildTitles,
    WorkItemTemplate? FeatureTemplate,
    WorkItemTemplate? StoryTemplate,
    WorkItemTemplate? BugTemplate);

/// <summary>The full description of a feature and the items written for it.</summary>
/// <param name="Description">The feature's description, or empty when the agent didn't write one.</param>
/// <param name="Bugs">Concrete defects found in the code; only in <see cref="RepositoryAnalysisMode.Improvements"/>.</param>
public sealed record WrittenFeature(string Description, IReadOnlyList<GeneratedWorkItem> Stories, IReadOnlyList<GeneratedWorkItem> Bugs);
