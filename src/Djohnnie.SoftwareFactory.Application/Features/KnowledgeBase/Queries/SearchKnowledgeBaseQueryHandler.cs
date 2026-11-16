using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>
/// Searches the index, then checks every passage against the database: the index lags behind (the indexer runs on a
/// schedule) and doesn't notice deleted blobs by itself, so passages of deleted files, or of files that now belong to
/// another repository, are dropped. The title, link and folder come from the database too, so they are always current.
/// </summary>
public sealed class SearchKnowledgeBaseQueryHandler(IKnowledgeSearchIndex index, IKnowledgeBaseStore store, IKnowledgeFileStorage storage, IOptions<KnowledgeSearchOptions> options)
    : IRequestHandler<SearchKnowledgeBaseQuery, Response<CollectionResult<KnowledgeSearchResult>>>
{
    public async Task<Response<CollectionResult<KnowledgeSearchResult>>> Handle(SearchKnowledgeBaseQuery request, CancellationToken cancellationToken)
    {
        if (!index.IsConfigured)
        {
            throw new DomainRuleException("Searching the knowledge base is not configured.");
        }

        var maxResults = Math.Max(1, options.Value.MaxResults);
        // Ask for more than needed, because some passages may be dropped below.
        var hits = await index.SearchAsync(request.Dto.Query.Trim(), request.Dto.RepositoryFullName, maxResults * 2, cancellationToken);
        if (hits.Count == 0)
        {
            return new(new([]));
        }

        var library = await store.GetLibraryAsync(request.Dto.RepositoryFullName, cancellationToken);
        var files = library.Files.ToDictionary(file => file.Id);
        var results = hits
            .Where(hit => files.ContainsKey(hit.FileId))
            .Select(hit =>
            {
                var file = files[hit.FileId];
                return new KnowledgeSearchResult(file.Id, file.Title, storage.GetUrl(file.Id), library.VirtualPath(file.FolderId), file.RepositoryFullName, hit.Text);
            })
            .Take(maxResults)
            .ToList();
        return new(new(results));
    }
}
