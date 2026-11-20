namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>The items of one type in a repository by their current status; every item is in exactly one number.</summary>
/// <param name="Draft">The items without a GitHub issue.</param>
/// <param name="Accepted">The items with an open GitHub issue, whether they are accepted or in progress.</param>
/// <param name="Closed">The items whose GitHub issue is closed as completed: the finished work.</param>
/// <param name="NotPlanned">The items whose GitHub issue is closed as not planned or as a duplicate; not part of <see cref="Total"/>.</param>
public sealed record WorkItemCounts(WorkItemType Type, int Draft, int Accepted, int Closed, int NotPlanned = 0)
{
    /// <summary>The planned work: every item except the ones that are <see cref="NotPlanned"/>, so progress is measured against it.</summary>
    public int Total
    {
        get
        {
            return Draft + Accepted + Closed;
        }
    }
}
