using Djohnnie.SoftwareFactory.Contracts.Agents;

namespace Djohnnie.SoftwareFactory.Contracts.AgentTemplates;

public sealed record SaveTemplateRequest(Guid Id, string FileName, AgentDefinition Agent);
public sealed record ApplyTemplateRequest(
    AgentTemplate Template, IReadOnlyList<string> Repositories, string FileName, bool Overwrite, string? CommitMessage);
