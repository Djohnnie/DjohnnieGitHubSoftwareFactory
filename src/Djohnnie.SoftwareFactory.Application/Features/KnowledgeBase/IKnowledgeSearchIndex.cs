namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

/// <summary>A passage the search index found: the file it belongs to (the blob name) and its text.</summary>
public sealed record KnowledgeSearchHit(Guid FileId, string Text);

/// <summary>Searches the passages of the knowledge base files.</summary>
public interface IKnowledgeSearchIndex
{
    bool IsConfigured { get; }

    /// <summary>The passages of global files and of <paramref name="repositoryFullName"/>'s files that best match the query, best first.</summary>
    Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(string query, string repositoryFullName, int size, CancellationToken cancellationToken);
}
