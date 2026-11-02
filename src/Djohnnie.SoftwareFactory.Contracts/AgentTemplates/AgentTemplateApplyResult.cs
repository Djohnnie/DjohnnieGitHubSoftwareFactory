namespace Djohnnie.SoftwareFactory.Contracts.AgentTemplates;

public enum AgentTemplateApplyOutcome
{
    Created,
    Updated,
    Unchanged,
    Skipped,
    Failed
}

public sealed record AgentTemplateApplyResult(
    string Repository, AgentTemplateApplyOutcome Outcome, string? Message = null,
    string? CommitSha = null, string? CommitUrl = null);
