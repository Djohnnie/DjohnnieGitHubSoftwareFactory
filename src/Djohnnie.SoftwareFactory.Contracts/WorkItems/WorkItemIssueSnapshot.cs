namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>What a work item takes over from its GitHub issue when it is refreshed.</summary>
/// <param name="Description">The issue body without DGHSF's hidden marker.</param>
/// <param name="Progress">Copilot's work on the issue; <c>null</c> keeps what is stored (GitHub didn't show the pull requests).</param>
/// <param name="IsNotPlanned">The issue is closed as not planned or as a duplicate, so the work wasn't done.</param>
public sealed record WorkItemIssueSnapshot(long IssueId, string IssueUrl, string Title, string Description, bool IsClosed, bool IsAssignedToCopilot,
    WorkItemProgressSnapshot? Progress = null, bool IsNotPlanned = false);
