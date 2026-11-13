using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

/// <summary>
/// Manages the global skills and enables or disables them in repositories. A skill is enabled in a repository when that
/// repository has its <c>.github/skills/{name}/SKILL.md</c>; enabling commits the file, disabling deletes it, both
/// directly on the default branch.
/// </summary>
public class SkillWorkflow(ISkillStore store, GitHubApiClient gitHub)
{
    /// <summary>Creates (empty id) or updates a skill after trimming its properties.</summary>
    /// <exception cref="DomainRuleException">The name or description is invalid, or the name is already used by another skill.</exception>
    public async Task<Skill> SaveAsync(Skill skill, CancellationToken cancellationToken = default)
    {
        var name = skill.Name.Trim();
        if (!Skill.IsValidName(name))
        {
            throw new DomainRuleException(
                $"'{name}' is not a valid skill name: use lowercase letters, digits and single hyphens (at most {Skill.MaxNameLength} characters).");
        }

        var description = skill.Description.Trim();
        if (description.Length == 0)
        {
            throw new DomainRuleException("A skill needs a description, so Copilot knows when to use it.");
        }

        return await store.SaveAsync(new Skill
        {
            Id = skill.Id,
            Name = name,
            Description = description,
            License = Optional(skill.License),
            Compatibility = Optional(skill.Compatibility),
            AllowedTools = Optional(skill.AllowedTools),
            Instructions = skill.Instructions.Replace("\r\n", "\n").Trim('\n')
        }, cancellationToken);
    }

    /// <summary>Every managed skill with its state in the repository, plus the repository's skill directories that aren't managed.</summary>
    public async Task<RepositorySkills> GetRepositorySkillsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var skills = await store.GetAllAsync(cancellationToken);
        var directories = (await gitHub.GetDirectoryAsync(repositoryFullName, Skill.Directory, cancellationToken))
            .Where(item => item.Type == "dir").Select(item => item.Name).ToHashSet(StringComparer.Ordinal);

        var states = await Task.WhenAll(skills.Select(async skill =>
        {
            if (!directories.Contains(skill.Name))
            {
                return new RepositorySkill(skill, SkillRepositoryStatus.Disabled);
            }

            var file = await gitHub.GetFileAsync(repositoryFullName, Skill.GetPath(skill.Name), cancellationToken);
            if (file is null)
            {
                return new RepositorySkill(skill, SkillRepositoryStatus.Disabled);
            }

            var status = SkillFileSerializer.AreEqual(file.Content, SkillFileSerializer.Serialize(skill))
                ? SkillRepositoryStatus.Enabled
                : SkillRepositoryStatus.Outdated;
            return new RepositorySkill(skill, status, file.HtmlUrl);
        }));

        var managed = skills.Select(skill => skill.Name).ToHashSet(StringComparer.Ordinal);
        var unmanaged = directories.Where(name => !managed.Contains(name)).Order(StringComparer.Ordinal).ToList();
        return new RepositorySkills(states, unmanaged);
    }

    /// <summary>Commits the skill's <c>SKILL.md</c> to the repository, or replaces it when it differs.</summary>
    /// <exception cref="DomainRuleException">The skill no longer exists.</exception>
    public async Task<SkillCommitResult> EnableAsync(string repositoryFullName, Guid skillId, CancellationToken cancellationToken = default)
    {
        var skill = await GetSkillAsync(skillId, cancellationToken);
        var path = Skill.GetPath(skill.Name);
        var content = SkillFileSerializer.Serialize(skill);

        var existing = await gitHub.GetFileAsync(repositoryFullName, path, cancellationToken);
        if (existing is not null && SkillFileSerializer.AreEqual(existing.Content, content))
        {
            return new SkillCommitResult(false);
        }

        var message = $"{(existing is null ? "Enable" : "Update")} skill {skill.Name}";
        var commit = await gitHub.PutFileAsync(repositoryFullName, path, content, message, existing?.Sha, cancellationToken);
        return new SkillCommitResult(true, commit.CommitSha, commit.CommitUrl);
    }

    /// <summary>Deletes the skill's <c>SKILL.md</c> from the repository; other files in its directory are left alone.</summary>
    /// <exception cref="DomainRuleException">The skill no longer exists.</exception>
    public async Task<SkillCommitResult> DisableAsync(string repositoryFullName, Guid skillId, CancellationToken cancellationToken = default)
    {
        var skill = await GetSkillAsync(skillId, cancellationToken);
        var path = Skill.GetPath(skill.Name);

        var existing = await gitHub.GetFileAsync(repositoryFullName, path, cancellationToken);
        if (existing is null)
        {
            return new SkillCommitResult(false);
        }

        var commit = await gitHub.DeleteFileAsync(repositoryFullName, path, $"Disable skill {skill.Name}", existing.Sha, cancellationToken);
        return new SkillCommitResult(true, commit.CommitSha, commit.CommitUrl);
    }

    private async Task<Skill> GetSkillAsync(Guid skillId, CancellationToken cancellationToken)
    {
        return await store.GetAsync(skillId, cancellationToken) ?? throw new DomainRuleException("The skill no longer exists.");
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
