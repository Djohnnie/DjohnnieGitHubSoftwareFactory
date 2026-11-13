namespace Djohnnie.SoftwareFactory.Contracts.Skills;

public sealed record SaveSkillRequest(Skill Skill);

public sealed record RepositorySkillRequest(string RepositoryFullName, Guid SkillId);
