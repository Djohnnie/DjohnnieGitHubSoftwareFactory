using System.ComponentModel;

namespace Djohnnie.SoftwareFactory.Contracts.TodoTasks;

/// <summary>What a TODO task does; every kind has one handler in the Tasks function app. Stored by name.</summary>
public enum TodoTaskKind
{
    /// <summary>Reads a repository and plans its epics and features (the first task of a run).</summary>
    [Description("Analyze repository")] AnalyzeRepository,

    /// <summary>Describes one feature planned by <see cref="AnalyzeRepository"/> and writes its user stories.</summary>
    [Description("Write feature work items")] WriteFeatureWorkItems
}
