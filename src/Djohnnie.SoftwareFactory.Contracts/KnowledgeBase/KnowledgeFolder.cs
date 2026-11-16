namespace Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;

/// <summary>
/// A virtual folder of the knowledge base; folders only exist in the database, the blobs are not organized in folders.
/// Every folder belongs to one repository and holds that repository's files; global files are never in a folder.
/// </summary>
public sealed record KnowledgeFolder
{
    public const int MaxNameLength = 100;

    public Guid Id { get; set; }

    /// <summary>The repository the folder belongs to (lowercase <c>owner/name</c>); it never changes.</summary>
    public string RepositoryFullName { get; set; } = string.Empty;

    /// <summary>The folder it is in, or <c>null</c> for a folder at the top of the library.</summary>
    public Guid? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public KnowledgeFolder Copy()
    {
        return this with { };
    }
}
