using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>A child item proposed by the Work Item Breakdown agent; nothing is stored until it is saved as a draft.</summary>
public sealed record GeneratedWorkItem(
    [property: Required, MaxLength(GeneratedWorkItem.MaxTitleLength)] string Title,
    [property: MaxLength(WorkItem.MaxDescriptionLength)] string Description)
{
    public const int MaxTitleLength = 256;

    /// <summary>The most items proposed by one run of the agent, and saved at once.</summary>
    public const int MaxPerRun = 20;
}

/// <summary>Asks the Work Item Breakdown agent to split a stored epic, feature or user story.</summary>
public sealed record GenerateChildWorkItemsRequest(string RepositoryFullName, int ParentId);

/// <param name="ChildType">The type of the proposed items.</param>
/// <param name="TemplateName">The template the items follow, or <c>null</c> when the child type has no templates.</param>
/// <param name="Items">The proposed items; empty when the agent had nothing to add.</param>
public sealed record GeneratedChildWorkItems(WorkItemType ChildType, string? TemplateName, IReadOnlyList<GeneratedWorkItem> Items);

/// <summary>Saves proposed items as drafts under their parent, all or none.</summary>
public sealed record SaveGeneratedWorkItemsRequest(string RepositoryFullName, int ParentId, IReadOnlyList<GeneratedWorkItem> Items);
