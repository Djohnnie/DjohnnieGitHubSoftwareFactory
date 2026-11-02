using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Agents;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

public static class AgentCatalog
{
    /// <summary>Loads the custom agents of a repository, sorted by name. Invalid files are listed with a warning description.</summary>
    public static async Task<List<AgentSummary>> LoadAsync(GitHubApiClient gitHub, string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var items = (await gitHub.GetDirectoryAsync(repositoryFullName, AgentDefinition.Directory, cancellationToken))
            .Where(i => i.Type == "file" && i.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

        var agents = await Task.WhenAll(items.Select(async item =>
        {
            var id = AgentDefinition.GetFileName(item.Name);
            try
            {
                var file = await gitHub.GetFileAsync(repositoryFullName, item.Path, cancellationToken);
                var agent = file is null ? null : AgentFileSerializer.Parse(file.Content);
                return new AgentSummary(id, string.IsNullOrWhiteSpace(agent?.Name) ? id : agent.Name, agent?.Description, agent?.Model,
                    agent?.Target is null or "github-copilot");
            }
            catch (FormatException)
            {
                return new AgentSummary(id, id, "Invalid agent file.", null, true);
            }
        }));

        return agents.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
