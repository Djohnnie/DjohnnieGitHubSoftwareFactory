using System.Text.Json.Serialization;

namespace Djohnnie.SoftwareFactory.Contracts.GitHub;

/// <summary>The number of Copilot code review comments with one value of a breakdown (e.g. severity "high").</summary>
public sealed record CopilotReviewCommentCount(string Name, int Evaluated, int Addressed);

/// <summary>One repository's Copilot pull request activity on one day, from GitHub's <c>repos-1-day</c> usage metrics report.</summary>
public sealed record CopilotRepositoryDay(
    DateOnly Day,
    int PullRequestsCreated,
    int CreatedByCopilot,
    int PullRequestsMerged,
    int MergedCreatedByCopilot,
    int ReviewedByCopilot,
    int ReviewCommentsEvaluated,
    int ReviewCommentsAddressed,
    IReadOnlyList<CopilotReviewCommentCount> ReviewCommentsBySeverity);

/// <summary>A repository's Copilot pull request activity from <paramref name="From"/> through <paramref name="To"/>.</summary>
/// <param name="Days">Only the days on which the repository had pull request activity, oldest first.</param>
/// <param name="FromPullRequests">
/// Worked out from the repository's pull requests and their reviews, because GitHub has no Copilot usage metrics for
/// personal accounts; then there are no review comment counts.
/// </param>
public sealed record CopilotRepositoryMetrics(DateOnly From, DateOnly To, IReadOnlyList<CopilotRepositoryDay> Days, bool FromPullRequests = false)
{
    public int PullRequestsCreated => Days.Sum(d => d.PullRequestsCreated);
    public int CreatedByCopilot => Days.Sum(d => d.CreatedByCopilot);
    public int PullRequestsMerged => Days.Sum(d => d.PullRequestsMerged);
    public int MergedCreatedByCopilot => Days.Sum(d => d.MergedCreatedByCopilot);
    public int ReviewedByCopilot => Days.Sum(d => d.ReviewedByCopilot);
    public int ReviewCommentsEvaluated => Days.Sum(d => d.ReviewCommentsEvaluated);
    public int ReviewCommentsAddressed => Days.Sum(d => d.ReviewCommentsAddressed);

    /// <summary>The review comments per severity over all days, most evaluated first.</summary>
    public IReadOnlyList<CopilotReviewCommentCount> ReviewCommentsBySeverity
    {
        get
        {
            return Days.SelectMany(d => d.ReviewCommentsBySeverity)
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CopilotReviewCommentCount(g.First().Name, g.Sum(c => c.Evaluated), g.Sum(c => c.Addressed)))
                .OrderByDescending(c => c.Evaluated)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}

/// <summary>The metered Copilot usage reports of GitHub's billing API.</summary>
public enum CopilotBillingReport
{
    /// <summary>Premium requests (request-based billing).</summary>
    PremiumRequests,

    /// <summary>AI credits (usage-based billing).</summary>
    AiCredits
}

/// <summary>One line of a billing usage report; amounts are in US dollars, as GitHub bills them.</summary>
public sealed record CopilotBillingUsageItem(
    [property: JsonPropertyName("product")] string? Product,
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("unitType")] string? UnitType,
    [property: JsonPropertyName("pricePerUnit")] decimal PricePerUnit,
    [property: JsonPropertyName("grossQuantity")] decimal GrossQuantity,
    [property: JsonPropertyName("grossAmount")] decimal GrossAmount,
    [property: JsonPropertyName("discountQuantity")] decimal DiscountQuantity,
    [property: JsonPropertyName("discountAmount")] decimal DiscountAmount,
    [property: JsonPropertyName("netQuantity")] decimal NetQuantity,
    [property: JsonPropertyName("netAmount")] decimal NetAmount);

/// <summary>A billing usage report of the account that owns a repository, for one month.</summary>
public sealed record CopilotBillingUsage(string Account, int Year, int Month, IReadOnlyList<CopilotBillingUsageItem> Items);

public sealed record CopilotBillingUsageRequest(string RepositoryFullName, bool OwnedByOrganization, CopilotBillingReport Report);
