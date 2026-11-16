namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>What a repository is about, for the Chat Suggestions agent.</summary>
/// <param name="FullName">The repository, e.g. <c>owner/name</c>.</param>
/// <param name="Mindmaps">The mindmaps as indented outlines, see <see cref="BackgroundAgents.MindmapsOutline"/>.</param>
/// <param name="ParentWorkItems">The epics, features and user stories as an outline, see <see cref="WorkItemBacklog.Outline"/>.</param>
public sealed record RepositoryOverview(string FullName, string Mindmaps, string ParentWorkItems);
