using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Epics;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class EpicWorkflow(IEpicStore store, GitHubApiClient gitHub)
{
    public async Task<EpicAcceptResult> AcceptAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default)
    {
        var epic = await FindAsync(repositoryFullName, id, cancellationToken);
        if (epic.Status != WorkItemStatus.Draft)
        {
            throw new InvalidOperationException("Only draft epics can be accepted.");
        }
        if (string.IsNullOrWhiteSpace(epic.Title) || string.IsNullOrWhiteSpace(epic.Description))
        {
            throw new InvalidOperationException("An epic needs a title and a description before it can be accepted.");
        }

        await gitHub.EnsureLabelAsync(repositoryFullName, Epic.Label, Epic.LabelColor, "Epic planned in DGHSF", cancellationToken);
        var issue = await gitHub.CreateIssueAsync(repositoryFullName, epic.Title.Trim(), IssueBody(epic), [Epic.Label], cancellationToken);
        epic = await store.MarkAcceptedAsync(repositoryFullName, id, issue.Number, issue.HtmlUrl, cancellationToken);
        return await TryAssignAsync(epic, cancellationToken);
    }

    public async Task<EpicAcceptResult> AssignAgentAsync(string repositoryFullName, int id, CancellationToken cancellationToken = default)
    {
        var epic = await FindAsync(repositoryFullName, id, cancellationToken);
        if (epic.Status != WorkItemStatus.Accepted || epic.IssueNumber is null)
        {
            throw new InvalidOperationException("Only accepted epics that are not assigned yet can be assigned.");
        }
        return await TryAssignAsync(epic, cancellationToken);
    }

    public static string IssueBody(Epic epic)
    {
        return $"{epic.Description.Trim()}\n\n<!-- dghsf:epic:{epic.Id} -->\n";
    }

    private async Task<EpicAcceptResult> TryAssignAsync(Epic epic, CancellationToken cancellationToken)
    {
        try
        {
            var assignment = new CopilotAssignment(epic.RepositoryFullName, epic.BaseBranch, epic.CustomInstructions, epic.CustomAgent, epic.Model);
            var issue = await gitHub.AssignCopilotAsync(epic.RepositoryFullName, epic.IssueNumber!.Value, assignment, cancellationToken);
            if (!GitHubApiClient.IsAssignedToCopilot(issue))
            {
                return new(epic, "GitHub did not assign Copilot. Check that Copilot cloud agent is enabled for this repository.");
            }
        }
        catch (Exception ex) when (ex is GitHubApiException or HttpRequestException)
        {
            return new(epic, ex.Message);
        }

        return new(await store.MarkAssignedAsync(epic.RepositoryFullName, epic.Id, cancellationToken), null);
    }

    private async Task<Epic> FindAsync(string repositoryFullName, int id, CancellationToken cancellationToken)
    {
        return await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new InvalidOperationException("The epic no longer exists.");
    }
}
