namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>The kinds of planning items. Each becomes a GitHub issue with its own label.</summary>
public enum WorkItemType
{
    /// <summary>Top-level structure; never assigned to an agent.</summary>
    Epic = 0,

    /// <summary>Always part of an epic; never assigned to an agent.</summary>
    Feature = 1,

    /// <summary>A user story; always part of a feature, and assigned to the Copilot cloud agent.</summary>
    Story = 2,

    /// <summary>Standalone or part of an epic, feature or story; assigned to the Copilot cloud agent.</summary>
    Bug = 3,

    /// <summary>Standalone or part of an epic, feature or story; assigned to the Copilot cloud agent.</summary>
    Task = 4
}
