using System.Text;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>The pull request of a work item (the one its progress is about): its diff, its discussion and feedback for Copilot.</summary>
public sealed class WorkItemPullRequests(IWorkItemStore store, GitHubApiClient gitHub)
{
    public const string CopilotMention = "@copilot";

    /// <summary>Reads the pull request, its files and its comments from GitHub (four or more requests); nothing is stored.</summary>
    public async Task<PullRequestChanges> GetChangesAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        var number = await PullRequestNumberAsync(repositoryFullName, id, cancellationToken);
        var pullRequest = await gitHub.GetPullRequestAsync(repositoryFullName, number, cancellationToken);
        var files = await gitHub.GetPullRequestFilesAsync(repositoryFullName, number, cancellationToken);
        var conversation = await gitHub.GetIssueCommentsAsync(repositoryFullName, number, cancellationToken);
        var lineComments = await gitHub.GetPullRequestReviewCommentsAsync(repositoryFullName, number, cancellationToken);

        var comments = conversation.Select(c => ToComment(c, onLine: false))
            .Concat(lineComments.Select(c => ToComment(c, onLine: true)))
            .OrderBy(c => c.CreatedAt)
            .ToList();

        return new(pullRequest.Number, pullRequest.HtmlUrl, pullRequest.Title, pullRequest.IsOpen, pullRequest.MergedAt is not null, pullRequest.Draft,
            pullRequest.User?.Login, pullRequest.Additions, pullRequest.Deletions,
            files.Select(f => new PullRequestFileChange(f.FileName, f.PreviousFileName, f.Status, f.Additions, f.Deletions, f.Patch, f.BlobUrl)).ToList(),
            pullRequest.ChangedFiles > files.Count && files.Count >= GitHubApiClient.MaxPullRequestFiles,
            comments);
    }

    private static PullRequestComment ToComment(GitHubComment comment, bool onLine)
    {
        return new(comment.User?.Login, comment.Body ?? string.Empty, comment.CreatedAt, comment.HtmlUrl,
            onLine ? comment.Path : null, onLine ? comment.Line ?? comment.OriginalLine : null);
    }

    /// <summary>
    /// Without line comments (and without requesting changes) the feedback becomes a comment in the pull request's
    /// conversation; otherwise one review with the line comments. Either way it mentions <c>@copilot</c> once, at the start,
    /// and a review's body repeats the line comments, so Copilot gets all feedback however it reads the review.
    /// </summary>
    public async Task<PullRequestFeedbackResult> SendFeedbackAsync(PullRequestFeedback feedback, CancellationToken cancellationToken = default)
    {
        var number = await PullRequestNumberAsync(feedback.RepositoryFullName, feedback.Id, cancellationToken);
        var pullRequest = await gitHub.GetPullRequestAsync(feedback.RepositoryFullName, number, cancellationToken);
        if (!pullRequest.IsOpen)
        {
            throw new DomainRuleException($"Pull request #{number} is {(pullRequest.MergedAt is null ? "closed" : "merged")}; Copilot only works on open pull requests.");
        }

        if (feedback.LineComments.Count == 0 && !feedback.RequestChanges)
        {
            return new(await gitHub.CreateIssueCommentAsync(feedback.RepositoryFullName, number, Mention(feedback.Body.Trim()), cancellationToken));
        }

        var comments = feedback.LineComments
            .Select(c => new GitHubReviewLineComment(c.Path, c.Line, c.Side == DiffSide.Left ? "LEFT" : "RIGHT", c.Body.Trim()))
            .ToList();
        try
        {
            return new(await gitHub.CreatePullRequestReviewAsync(feedback.RepositoryFullName, number, ReviewBody(feedback), comments, feedback.RequestChanges, cancellationToken));
        }
        catch (GitHubApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            throw new DomainRuleException($"GitHub didn't accept the review; the pull request may have changed since you loaded it, so reload the changes and try again. ({ex.Message})");
        }
    }

    /// <summary>The feedback with <c>@copilot</c> in front, unless it already starts with it.</summary>
    public static string Mention(string text)
    {
        return text.StartsWith(CopilotMention, StringComparison.OrdinalIgnoreCase) ? text : $"{CopilotMention} {text}".TrimEnd();
    }

    public static string ReviewBody(PullRequestFeedback feedback)
    {
        var body = new StringBuilder(Mention(feedback.Body.Trim()));
        if (feedback.LineComments.Count > 0)
        {
            body.AppendLine().AppendLine();
            body.AppendLine(string.IsNullOrWhiteSpace(feedback.Body)
                ? "Please address the comments on these lines:"
                : "Please also address the comments on these lines:");
            foreach (var comment in feedback.LineComments)
            {
                var side = comment.Side == DiffSide.Left ? " (removed line)" : string.Empty;
                body.AppendLine($"- `{comment.Path}` line {comment.Line}{side}: {comment.Body.Trim().ReplaceLineEndings(" ")}");
            }
        }
        return body.ToString().TrimEnd();
    }

    private async Task<int> PullRequestNumberAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(repositoryFullName, id, cancellationToken)
            ?? throw new DomainRuleException("The work item no longer exists.");
        return item.PullRequestNumber
            ?? throw new DomainRuleException($"This {item.Type.DisplayName.ToLowerInvariant()} has no pull request yet. Refresh it from GitHub once Copilot opened one.");
    }
}
