using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.Skills;

/// <summary>
/// A GitHub Copilot agent skill, managed globally in DGHSF. Its properties are stored in the database; enabling it in a
/// repository commits them as <c>.github/skills/{Name}/SKILL.md</c> (YAML frontmatter plus the Markdown instructions).
/// See https://docs.github.com/en/copilot/concepts/agents/about-agent-skills.
/// </summary>
public sealed record Skill
{
    public const string Directory = ".github/skills";
    public const string FileName = "SKILL.md";
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 1_024;
    public const int MaxLicenseLength = 200;
    public const int MaxCompatibilityLength = 500;
    public const int MaxAllowedToolsLength = 1_000;
    public const int MaxInstructionsLength = 30_000;

    public Guid Id { get; set; }

    /// <summary>The skill name and the name of its directory: lowercase letters, digits and single hyphens; unique across skills.</summary>
    [Required, MaxLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>What the skill does and when Copilot should use it.</summary>
    [Required, MaxLength(MaxDescriptionLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>The optional license of the skill, e.g. a license name or the name of a bundled license file.</summary>
    [MaxLength(MaxLicenseLength)]
    public string? License { get; set; }

    /// <summary>Optional environment requirements, e.g. required tools or network access.</summary>
    [MaxLength(MaxCompatibilityLength)]
    public string? Compatibility { get; set; }

    /// <summary>The optional, space-separated list of tools the skill may use without asking (<c>allowed-tools</c>).</summary>
    [MaxLength(MaxAllowedToolsLength)]
    public string? AllowedTools { get; set; }

    /// <summary>The Markdown instructions below the frontmatter.</summary>
    [MaxLength(MaxInstructionsLength)]
    public string Instructions { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The skill's directory in a repository.</summary>
    public static string GetDirectory(string name)
    {
        return $"{Directory}/{name}";
    }

    /// <summary>The skill's <c>SKILL.md</c> file in a repository.</summary>
    public static string GetPath(string name)
    {
        return $"{GetDirectory(name)}/{FileName}";
    }

    /// <summary>Whether a name is a valid skill name: 1-64 lowercase letters, digits and hyphens, not starting or ending with one, without consecutive hyphens.</summary>
    public static bool IsValidName(string? value)
    {
        return !string.IsNullOrEmpty(value)
            && value.Length <= MaxNameLength
            && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
            && !value.StartsWith('-')
            && !value.EndsWith('-')
            && !value.Contains("--", StringComparison.Ordinal);
    }

    /// <summary>Turns a free text into a valid skill name (or an empty string).</summary>
    public static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        return slug.Length > MaxNameLength ? slug[..MaxNameLength].TrimEnd('-') : slug;
    }

    /// <summary>An editable copy.</summary>
    public Skill Copy()
    {
        return this with { };
    }
}
