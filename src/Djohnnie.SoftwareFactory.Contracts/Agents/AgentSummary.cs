namespace Djohnnie.SoftwareFactory.Contracts.Agents;

/// <param name="Id">The agent id GitHub expects: the file name without <c>.agent.md</c>.</param>
/// <param name="AvailableOnGitHub">Whether the Copilot cloud agent can use it (not a VS Code only agent).</param>
public sealed record AgentSummary(string Id, string Name, string? Description, string? Model, bool AvailableOnGitHub);

