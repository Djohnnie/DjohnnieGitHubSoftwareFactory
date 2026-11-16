using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>
/// Hybrid search (keywords and vectors) in the Azure AI Search index of the knowledge base. The index vectorizes the
/// query itself (its vectorizer), so DGHSF doesn't call an embedding model. The field names must match
/// <c>docs/knowledge-base-search/index.json</c>.
/// </summary>
public sealed class AzureAiKnowledgeSearchIndex(IOptions<KnowledgeSearchOptions> options) : IKnowledgeSearchIndex
{
    public const string FileIdField = "file_id";
    public const string ChunkField = "chunk";
    public const string VectorField = "text_vector";
    public const string ScopeField = "scope";
    public const string RepositoryField = "repository";

    private readonly Lazy<SearchClient> _client = new(() =>
    {
        var value = options.Value;
        if (!value.IsConfigured)
        {
            throw new InvalidOperationException($"{KnowledgeSearchOptions.SectionName} is not configured.");
        }

        return new SearchClient(new Uri(value.Endpoint!), value.IndexName, new AzureKeyCredential(value.ApiKey!));
    });

    public bool IsConfigured
    {
        get
        {
            return options.Value.IsConfigured;
        }
    }

    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(string query, string repositoryFullName, int size, CancellationToken cancellationToken)
    {
        var search = BuildOptions(query, repositoryFullName, size, options.Value.SemanticConfiguration);
        var response = await _client.Value.SearchAsync<SearchDocument>(query, search, cancellationToken);
        var hits = new List<KnowledgeSearchHit>();
        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            // The blob name is the file id; anything else (e.g. a blob that isn't a knowledge base file) is skipped.
            if (result.Document.TryGetValue(FileIdField, out var id) && Guid.TryParse(id as string, out var fileId)
                && result.Document.TryGetValue(ChunkField, out var chunk) && chunk is string text && !string.IsNullOrWhiteSpace(text))
            {
                hits.Add(new KnowledgeSearchHit(fileId, text));
            }
        }

        return hits;
    }

    /// <summary>The search request: hybrid keywords and vectors, only the global passages and the repository's.</summary>
    public static SearchOptions BuildOptions(string query, string repositoryFullName, int size, string? semanticConfiguration)
    {
        var search = new SearchOptions
        {
            Size = size,
            Filter = Filter(repositoryFullName),
            VectorSearch = new VectorSearchOptions
            {
                Queries = { new VectorizableTextQuery(query) { KNearestNeighborsCount = size, Fields = { VectorField } } }
            }
        };
        search.Select.Add(FileIdField);
        search.Select.Add(ChunkField);
        if (!string.IsNullOrWhiteSpace(semanticConfiguration))
        {
            search.QueryType = SearchQueryType.Semantic;
            search.SemanticSearch = new SemanticSearchOptions { SemanticConfigurationName = semanticConfiguration };
        }

        return search;
    }

    /// <summary>
    /// The OData filter on the passages' scope and repository (from the blob metadata); the values are escaped. Passages
    /// without a scope come from files uploaded before files could belong to a repository, so they are global too (and
    /// the handler checks every passage against the database anyway).
    /// </summary>
    public static string Filter(string repositoryFullName)
    {
        // SearchFilter.Create quotes every interpolated value, so the field names (ScopeField, RepositoryField) are written out.
        var repository = repositoryFullName.Trim().ToLowerInvariant();
        return SearchFilter.Create($"scope eq {AzureBlobKnowledgeFileStorage.GlobalScope} or scope eq null or repository eq {repository}");
    }
}
