using System.Net;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public partial class WorkItemServiceTests
{
    /// <summary>An accepted task whose refresh found Copilot's pull request #7.</summary>
    private async Task<WorkItem> TaskWithPullRequestAsync()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");
        _gitHub.LinkedPullRequests[42] = [new(7, "Task", "OPEN", "copilot/fix-42")];
        var refreshed = await RefreshAssignedTaskAsync(task);
        Assert.Equal(7, refreshed.PullRequestNumber);
        return refreshed;
    }

    [Fact]
    public async Task PullRequestChanges_ReturnTheFilesAndTheDiscussionInOrder()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.PullRequestFiles.Add(("src/App.cs", "@@ -1,2 +1,2 @@\n-old\n+new\n same"));
        _gitHub.PullRequestFiles.Add(("logo.png", null));
        _gitHub.ConversationComments.Add("Looks good so far");
        _gitHub.LineComments.Add(("src/App.cs", 1, "Rename this"));

        var changes = await _service.GetPullRequestChangesAsync(Repo, task.Id, _cancellationToken);

        Assert.Equal(7, changes.Number);
        Assert.True(changes.IsOpen);
        Assert.Equal(["src/App.cs", "logo.png"], changes.Files.Select(f => f.FileName));
        Assert.Null(changes.Files[1].Patch);
        Assert.False(changes.FilesTruncated);
        Assert.Collection(changes.Comments,
            first => Assert.Equal(("Looks good so far", (string?)null), (first.Body, first.Path)),
            second => Assert.Equal(("Rename this", "src/App.cs", (int?)1), (second.Body, second.Path, second.Line)));
    }

    [Fact]
    public async Task PullRequestChanges_WithoutAPullRequest_AreRefused()
    {
        var task = await AcceptAsync(WorkItemType.Task, "Task");

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.GetPullRequestChangesAsync(Repo, task.Id, _cancellationToken));
        Assert.Contains("no pull request yet", error.Message);
    }

    [Fact]
    public async Task Feedback_WithoutLineComments_IsAConversationCommentThatMentionsCopilot()
    {
        var task = await TaskWithPullRequestAsync();

        var result = await _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, "  Please add tests.  ", []), _cancellationToken);

        Assert.Equal("https://github.com/owner/repo/pull/7#issuecomment-1", result.Url);
        var posted = Assert.Single(_gitHub.Requests, r => r.Method == "POST" && r.Path == "/repos/owner/repo/issues/7/comments");
        Assert.Equal("@copilot Please add tests.", JsonDocument.Parse(posted.Body!).RootElement.GetProperty("body").GetString());
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path.EndsWith("/reviews"));
    }

    [Fact]
    public void Feedback_ThatAlreadyMentionsCopilot_IsNotMentionedTwice()
    {
        Assert.Equal("@Copilot fix it", WorkItemPullRequests.Mention("@Copilot fix it"));
        Assert.Equal("@copilot fix it", WorkItemPullRequests.Mention("fix it"));
    }

    [Fact]
    public async Task Feedback_WithLineComments_IsOneReviewThatAlsoListsThemInItsBody()
    {
        var task = await TaskWithPullRequestAsync();
        var feedback = new PullRequestFeedback(Repo, task.Id, "Nearly there.",
            [new("src/App.cs", 1, DiffSide.Right, "Rename this"), new("src/Old.cs", 4, DiffSide.Left, "Keep\nthis line")], RequestChanges: true);

        var result = await _service.SendPullRequestFeedbackAsync(feedback, _cancellationToken);

        Assert.Equal("https://github.com/owner/repo/pull/7#pullrequestreview-9", result.Url);
        var posted = Assert.Single(_gitHub.Requests, r => r.Method == "POST" && r.Path == "/repos/owner/repo/pulls/7/reviews");
        var review = JsonDocument.Parse(posted.Body!).RootElement;
        Assert.Equal("REQUEST_CHANGES", review.GetProperty("event").GetString());
        Assert.Equal("""
            @copilot Nearly there.

            Please also address the comments on these lines:
            - `src/App.cs` line 1: Rename this
            - `src/Old.cs` line 4 (removed line): Keep this line
            """.ReplaceLineEndings("\n"), review.GetProperty("body").GetString()!.ReplaceLineEndings("\n"));
        Assert.Collection(review.GetProperty("comments").EnumerateArray(),
            first => Assert.Equal(("src/App.cs", 1, "RIGHT", "Rename this"),
                (first.GetProperty("path").GetString(), first.GetProperty("line").GetInt32(), first.GetProperty("side").GetString(), first.GetProperty("body").GetString())),
            second => Assert.Equal("LEFT", second.GetProperty("side").GetString()));
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method == "POST" && r.Path.EndsWith("/issues/7/comments"));
    }

    [Fact]
    public async Task Feedback_WithOnlyLineComments_IsACommentReview()
    {
        var task = await TaskWithPullRequestAsync();

        await _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, string.Empty, [new("src/App.cs", 1, DiffSide.Right, "Rename this")]), _cancellationToken);

        var review = JsonDocument.Parse(_gitHub.Requests.Single(r => r.Path.EndsWith("/reviews")).Body!).RootElement;
        Assert.Equal("COMMENT", review.GetProperty("event").GetString());
        Assert.StartsWith("@copilot\n\nPlease address the comments on these lines:", review.GetProperty("body").GetString()!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Feedback_OnALineThatIsNoLongerInTheDiff_AsksToReload()
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.ReviewFailure = HttpStatusCode.UnprocessableEntity;

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.SendPullRequestFeedbackAsync(
            new(Repo, task.Id, string.Empty, [new("src/App.cs", 99, DiffSide.Right, "Here")]), _cancellationToken));
        Assert.Contains("reload the changes", error.Message);
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("closed")]
    public async Task Feedback_OnAClosedPullRequest_IsRefused(string state)
    {
        var task = await TaskWithPullRequestAsync();
        _gitHub.PullRequestStates[7] = state;

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, "Fix it", []), _cancellationToken));
        Assert.Contains(state, error.Message);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method == "POST" && (r.Path.EndsWith("/comments") || r.Path.EndsWith("/reviews")));
    }

    [Fact]
    public async Task Feedback_WithoutAnyText_IsInvalid()
    {
        var task = await TaskWithPullRequestAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.SendPullRequestFeedbackAsync(new(Repo, task.Id, "   ", []), _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.SendPullRequestFeedbackAsync(
            new(Repo, task.Id, "Fix", [new("src/App.cs", 0, DiffSide.Right, "Here")]), _cancellationToken));
    }
}
