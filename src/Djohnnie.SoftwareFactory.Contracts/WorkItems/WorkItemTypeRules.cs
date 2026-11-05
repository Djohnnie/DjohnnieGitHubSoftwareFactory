namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>Labels, names and hierarchy rules per work item type.</summary>
public static class WorkItemTypeRules
{
    public static IReadOnlyList<WorkItemType> All { get; } = Enum.GetValues<WorkItemType>();

    extension(WorkItemType type)
    {
        /// <summary>The GitHub label that marks the kind of issue.</summary>
        public string Label
        {
            get
            {
                return type switch
                {
                    WorkItemType.Epic => "epic",
                    WorkItemType.Feature => "feature",
                    WorkItemType.Story => "story",
                    WorkItemType.Bug => "bug",
                    WorkItemType.Task => "task",
                    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
                };
            }
        }

        /// <summary>The label color (hex, without <c>#</c>) used when DGHSF creates the label.</summary>
        public string LabelColor
        {
            get
            {
                return type switch
                {
                    WorkItemType.Epic => "6F42C1",
                    WorkItemType.Feature => "1D76DB",
                    WorkItemType.Story => "0E8A16",
                    WorkItemType.Bug => "D73A4A",
                    WorkItemType.Task => "FBCA04",
                    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
                };
            }
        }

        public string DisplayName
        {
            get
            {
                return type switch
                {
                    WorkItemType.Story => "User story",
                    _ => type.ToString()
                };
            }
        }

        public string PluralName
        {
            get
            {
                return type switch
                {
                    WorkItemType.Story => "User stories",
                    _ => $"{type}s"
                };
            }
        }

        /// <summary>Only stories, bugs and tasks are assigned to the Copilot cloud agent; epics and features only give structure.</summary>
        public bool CanBeAssigned
        {
            get
            {
                return type is WorkItemType.Story or WorkItemType.Bug or WorkItemType.Task;
            }
        }

        /// <summary>Features must be part of an epic, stories part of a feature.</summary>
        public bool RequiresParent
        {
            get
            {
                return type is WorkItemType.Feature or WorkItemType.Story;
            }
        }

        /// <summary>The types an item of this type can be a child of.</summary>
        public IReadOnlyList<WorkItemType> ParentTypes
        {
            get
            {
                return type switch
                {
                    WorkItemType.Feature => [WorkItemType.Epic],
                    WorkItemType.Story => [WorkItemType.Feature],
                    WorkItemType.Bug or WorkItemType.Task => [WorkItemType.Epic, WorkItemType.Feature, WorkItemType.Story],
                    _ => []
                };
            }
        }

        /// <summary>The types that can be a child of an item of this type.</summary>
        public IReadOnlyList<WorkItemType> ChildTypes
        {
            get
            {
                var parent = type;
                return All.Where(child => child.ParentTypes.Contains(parent)).ToList();
            }
        }

        public bool CanHaveParent(WorkItemType parentType)
        {
            return type.ParentTypes.Contains(parentType);
        }
    }
}
