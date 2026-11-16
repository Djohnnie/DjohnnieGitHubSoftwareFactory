namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>
/// The Azure AI Search index that holds the passages (chunks with embeddings) of the knowledge base files, filled by an
/// indexer on the knowledge base container (see <c>docs/knowledge-base-search</c>). Unconfigured means the chat agents
/// don't get the <c>search_knowledge_base</c> tool.
/// </summary>
public sealed class KnowledgeSearchOptions
{
    public const string SectionName = "KnowledgeSearch";

    /// <summary>The search service URL, like <c>https://my-search.search.windows.net</c>.</summary>
    public string? Endpoint { get; set; }

    /// <summary>A query key of the search service (an admin key works too, but isn't needed).</summary>
    public string? ApiKey { get; set; }

    public string IndexName { get; set; } = "dghsf-knowledge-base";

    /// <summary>The index's semantic configuration, to rerank the results with the semantic ranker; empty to leave it out.</summary>
    public string? SemanticConfiguration { get; set; }

    /// <summary>The most passages one search returns to an agent.</summary>
    public int MaxResults { get; set; } = 5;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(IndexName);
}
