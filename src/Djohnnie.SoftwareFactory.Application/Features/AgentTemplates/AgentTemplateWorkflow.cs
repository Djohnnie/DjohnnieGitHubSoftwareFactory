using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public class AgentTemplateWorkflow(AgentTemplateStore store, GitHubApiClient gitHub)
{
    /// <summary>Creates (<paramref name="id"/> 0) or updates a template from an agent definition.</summary>
    /// <exception cref="FormatException">The agent cannot be serialized (for example invalid MCP server YAML).</exception>
    /// <exception cref="InvalidOperationException">The file name is invalid or already used by another template.</exception>
    public async Task<AgentTemplate> SaveAsync(int id, string fileName, AgentDefinition agent, CancellationToken cancellationToken = default)
    {
        fileName = fileName.Trim();
        if (!AgentDefinition.IsValidFileName(fileName))
        {
            throw new InvalidOperationException($"'{fileName}' is not a valid agent file name.");
        }

        if (agent.Prompt.Length > AgentDefinition.MaxPromptLength)
        {
            throw new InvalidOperationException($"The instructions are longer than {AgentDefinition.MaxPromptLength:N0} characters.");
        }

        var content = AgentFileSerializer.Serialize(agent);

        var description = agent.Description.Trim();
        return await store.SaveAsync(new AgentTemplate
        {
            Id = id,
            FileName = fileName,
            Name = string.IsNullOrWhiteSpace(agent.Name) ? fileName : agent.Name.Trim(),
            Description = description.Length > 2_000 ? description[..2_000] : description,
            Content = content
        }, cancellationToken);
    }

    /// <summary>
    /// Commits the template as <c>.github/agents/{fileName}.agent.md</c> to the default branch of every repository.
    /// An existing, different file is only replaced when <paramref name="overwrite"/> is set. One failing repository
    /// does not stop the others.
    /// </summary>
    public async Task<List<AgentTemplateApplyResult>> ApplyAsync(
        AgentTemplate template, IEnumerable<string> repositories, string fileName, bool overwrite,
        string? commitMessage = null, IProgress<AgentTemplateApplyResult>? progress = null, CancellationToken cancellationToken = default)
    {
        fileName = fileName.Trim();
        if (!AgentDefinition.IsValidFileName(fileName))
        {
            throw new InvalidOperationException($"'{fileName}' is not a valid agent file name.");
        }

        var path = AgentDefinition.GetPath(fileName);
        var results = new List<AgentTemplateApplyResult>();
        foreach (var repository in repositories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var result = await ApplyAsync(template, repository, path, overwrite, commitMessage, cancellationToken);
            results.Add(result);
            progress?.Report(result);
        }

        return results;
    }

    private async Task<AgentTemplateApplyResult> ApplyAsync(
        AgentTemplate template, string repository, string path, bool overwrite, string? commitMessage, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await gitHub.GetFileAsync(repository, path, cancellationToken);
            if (existing is not null && Normalize(existing.Content) == Normalize(template.Content))
            {
                return new(repository, AgentTemplateApplyOutcome.Unchanged, $"{path} is already up to date.");
            }

            if (existing is not null && !overwrite)
            {
                return new(repository, AgentTemplateApplyOutcome.Skipped, $"{path} already exists.");
            }

            var message = string.IsNullOrWhiteSpace(commitMessage)
                ? $"{(existing is null ? "Create" : "Update")} custom agent {template.Name} from template"
                : commitMessage.Trim();
            var commit = await gitHub.PutFileAsync(repository, path, template.Content, message, existing?.Sha, cancellationToken);
            return new(repository, existing is null ? AgentTemplateApplyOutcome.Created : AgentTemplateApplyOutcome.Updated,
                null, commit.CommitSha, commit.CommitUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or GitHubApiException)
        {
            return new(repository, AgentTemplateApplyOutcome.Failed, ex.Message);
        }
    }

    private static string Normalize(string content) => content.Replace("\r\n", "\n").TrimEnd();
}
