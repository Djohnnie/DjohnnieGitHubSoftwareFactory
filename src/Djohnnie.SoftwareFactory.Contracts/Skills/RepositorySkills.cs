namespace Djohnnie.SoftwareFactory.Contracts.Skills;

public enum SkillRepositoryStatus
{
    /// <summary>The repository has no <c>SKILL.md</c> for the skill.</summary>
    Disabled,

    /// <summary>The repository's <c>SKILL.md</c> matches the skill.</summary>
    Enabled,

    /// <summary>The repository has a <c>SKILL.md</c> for the skill, but it differs from the managed skill (edited on either side).</summary>
    Outdated
}

/// <summary>A managed skill and whether it is enabled in a repository.</summary>
public sealed record RepositorySkill(Skill Skill, SkillRepositoryStatus Status, string? FileUrl = null);

/// <param name="Skills">Every managed skill, sorted by name.</param>
/// <param name="UnmanagedSkills">The names of skill directories in the repository that don't belong to a managed skill.</param>
public sealed record RepositorySkills(IReadOnlyList<RepositorySkill> Skills, IReadOnlyList<string> UnmanagedSkills);

/// <summary>The result of enabling, updating or disabling a skill in a repository.</summary>
/// <param name="Changed">Whether a commit was made; <c>false</c> when the repository already was in the requested state.</param>
public sealed record SkillCommitResult(bool Changed, string? CommitSha = null, string? CommitUrl = null);
