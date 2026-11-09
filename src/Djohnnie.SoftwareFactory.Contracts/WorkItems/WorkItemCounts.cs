namespace Djohnnie.SoftwareFactory.Contracts.WorkItems;

/// <summary>The items of one type in a repository by their current status; every item is in exactly one number.</summary>
/// <param name="Draft">The items without a GitHub issue.</param>
/// <param name="Accepted">The items with an open GitHub issue, whether they are accepted or in progress.</param>
/// <param name="Closed">The items whose GitHub issue is closed.</param>
public sealed record WorkItemCounts(WorkItemType Type, int Draft, int Accepted, int Closed)
{
    public int Total
    {
        get
        {
            return Draft + Accepted + Closed;
        }
    }
}
