using Djohnnie.SoftwareFactory.Contracts.Skills;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

/// <summary>The globally managed Copilot skills and their state per repository.</summary>
public sealed class SkillService(ISender mediator)
{
    public async Task<List<Skill>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetSkillsQuery(new()), cancellationToken)).Dto.Items.ToList();
    }

    public async Task<Skill?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetSkillQuery(new(id)), cancellationToken)).Dto;
    }

    public async Task<Skill> SaveAsync(Skill skill, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new SaveSkillCommand(new(skill)), cancellationToken)).Dto;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await mediator.Send(new DeleteSkillCommand(new(id)), cancellationToken);
    }

    public async Task<RepositorySkills> GetRepositorySkillsAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new GetRepositorySkillsQuery(new(repositoryFullName)), cancellationToken)).Dto;
    }

    public async Task<SkillCommitResult> EnableAsync(string repositoryFullName, Guid skillId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new EnableSkillCommand(new(repositoryFullName, skillId)), cancellationToken)).Dto;
    }

    public async Task<SkillCommitResult> DisableAsync(string repositoryFullName, Guid skillId, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new DisableSkillCommand(new(repositoryFullName, skillId)), cancellationToken)).Dto;
    }

    /// <summary>The <c>SKILL.md</c> that enabling the skill commits.</summary>
    public static string Render(Skill skill)
    {
        return SkillFileSerializer.Serialize(skill);
    }
}
