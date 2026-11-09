namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>What a work item takes over from its GitHub issue when it is refreshed.</summary>
/// <param name="Description">The issue body without DGHSF's hidden marker.</param>
public sealed record WorkItemIssueSnapshot(long IssueId, string IssueUrl, string Title, string Description, bool IsClosed, bool IsAssignedToCopilot);
