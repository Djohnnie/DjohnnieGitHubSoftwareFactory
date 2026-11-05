using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

/// <summary>
/// A GitHub repository the app has worked with. Everything that belongs to a repository
/// (epics, agents, ...) references this row; it cannot be deleted while anything still does.
/// </summary>
public sealed class Repository
{
    public const int MaxFullNameLength = 200;

    public int Id { get; set; }

    /// <summary>The repository as <c>owner/name</c>, in lower case: GitHub names are case-insensitive.</summary>
    [Required, MaxLength(MaxFullNameLength)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>The repository name without its owner.</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>When the repository was last opened in the app, or <c>null</c> if it never was (e.g. only marked as favorite).</summary>
    public DateTimeOffset? LastAccessedAt { get; set; }

    public bool IsFavorite { get; set; }

    public List<Epic> Epics { get; set; } = [];
    public List<Agent> Agents { get; set; } = [];

    public static string Normalize(string fullName)
    {
        return fullName.Trim().ToLowerInvariant();
    }

    public static Repository Create(string fullName)
    {
        var normalized = Normalize(fullName);
        return new Repository { FullName = normalized, Name = normalized[(normalized.IndexOf('/') + 1)..] };
    }
}
