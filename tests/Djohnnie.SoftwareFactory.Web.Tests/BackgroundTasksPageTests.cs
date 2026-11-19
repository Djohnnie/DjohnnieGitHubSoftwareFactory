using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class BackgroundTasksPageTests : BunitContext
{
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;

    public BackgroundTasksPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddDataProtection();
        Services.AddLogging();
        AddAuthorization().SetAuthorized("octocat");
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<RepositoryContext>();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddApplication();
    }

    [Fact]
    public async Task Page_ShowsTheRunsOfTheRepository_NewestFirst_WithCancelForAnActiveRun()
    {
        await Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo", null, false, false, false, null, "main", null));
        var store = Services.GetRequiredService<ITodoTaskStore>();
        await store.CreateRunAsync("owner/other", new NewTodoTask(TodoTaskKind.AnalyzeRepository, "Other repository", "{}"), null, 3, _cancellationToken);
        await store.CreateRunAsync("owner/repo", new NewTodoTask(TodoTaskKind.AnalyzeRepository, "Analyze the repository (improvements)", "{}"), "octocat", 3, _cancellationToken);

        Render<MudPopoverProvider>();
        var cut = Render<BackgroundTasks>();

        cut.WaitForAssertion(() => Assert.Contains("Analyze the repository (improvements)", cut.Markup));
        Assert.DoesNotContain("Other repository", cut.Markup);
        Assert.Contains("Pending", cut.Markup);
        Assert.Contains("by octocat", cut.Markup);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Cancel");
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Discard drafts");
    }

    [Fact]
    public async Task Page_ShowsTheAgentWorkersRunsOfTheRepository_WithTheirWorkItemCostAndPullRequest()
    {
        await Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo", null, false, false, false, null, "main", null));
        var task = await Services.GetRequiredService<IWorkItemStore>().SaveAsync(new Contracts.WorkItems.WorkItem
        {
            RepositoryFullName = "owner/repo", Type = Contracts.WorkItems.WorkItemType.Task, Title = "Add the endpoint", Description = "An endpoint.",
            Executor = Contracts.WorkItems.AgentExecutor.DghsfWorker
        }, _cancellationToken);
        var runs = Services.GetRequiredService<IAgentRunStore>();
        var finished = await runs.CreateAsync("owner/repo", task.Id, null, 2, _cancellationToken);
        await using (var db = await Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(_cancellationToken))
        {
            var run = await db.AgentRuns.SingleAsync(r => r.Id == finished.Id, _cancellationToken);
            run.Status = Contracts.AgentRuns.AgentRunStatus.Succeeded;
            run.NanoAiu = 12_500_000_000;
            run.Models = "claude-sonnet-4.5";
            run.PullRequestNumber = 7;
            run.PullRequestUrl = "https://github.com/owner/repo/pull/7";
            run.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-25);
            run.CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-8);
            run.Attempts = 2;
            // A retry is a second run of the same task.
            db.AgentRunAttempts.Add(new DataAccess.Sql.Entities.AgentRunAttempt
            {
                Id = Guid.NewGuid(), AgentRunId = run.Id, Attempt = 1, Status = Contracts.AgentRuns.AgentRunStatus.Failed, Error = "git clone failed (128).",
                UsageComplete = true, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-25), CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-24)
            });
            db.AgentRunAttempts.Add(new DataAccess.Sql.Entities.AgentRunAttempt
            {
                Id = Guid.NewGuid(), AgentRunId = run.Id, Attempt = 2, Kind = Contracts.AgentRuns.AgentRunKind.Revise,
                Status = Contracts.AgentRuns.AgentRunStatus.Succeeded, NanoAiu = 12_500_000_000,
                Models = "claude-sonnet-4.5", PullRequestNumber = 7, PullRequestUrl = "https://github.com/owner/repo/pull/7", UsageComplete = true,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20), CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-8)
            });
            await db.SaveChangesAsync(_cancellationToken);
        }
        // Another run on the same item is a run of its task, not a task of its own.
        await runs.CreateAsync("owner/repo", task.Id, null, 2, _cancellationToken);
        await runs.CreateAsync("owner/other", Guid.NewGuid(), null, 2, _cancellationToken);

        Render<MudPopoverProvider>();
        var cut = Render<BackgroundTasks>();

        cut.WaitForAssertion(() => Assert.Contains("Agent worker tasks", cut.Markup));
        var waitingTask = Assert.Single(cut.FindAll(".agent-task"));

        // Collapsed to the latest status, the work item and the totals; the runs are listed once it is expanded.
        Assert.Contains("Waiting", waitingTask.QuerySelector(".run-toggle")!.TextContent);
        Assert.Contains("Add the endpoint", waitingTask.QuerySelector(".run-toggle")!.TextContent);
        Assert.Contains(waitingTask.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Cancel");
        Assert.Contains("12.5 AI credits", waitingTask.TextContent);
        Assert.Contains("#7", waitingTask.TextContent);
        Assert.Equal("false", waitingTask.QuerySelector(".run-toggle")!.GetAttribute("aria-expanded"));

        cut.Find(".agent-task .run-toggle").Click();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find(".agent-task .run-toggle").GetAttribute("aria-expanded")));
        var rows = cut.FindComponent<MudTable<Contracts.AgentRuns.AgentRunAttempt>>().FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        // Newest run first, each with what it did.
        Assert.Contains("Revision", rows[0].TextContent);
        Assert.Contains("Implementation", rows[1].TextContent);
        Assert.Contains("Succeeded", rows[0].TextContent);
        Assert.Contains("12 min", rows[0].TextContent);
        Assert.Contains("#7", rows[0].TextContent);
        Assert.Contains("Failed", rows[1].TextContent);
        Assert.Contains("git clone failed", rows[1].TextContent);
    }

    [Fact]
    public async Task Run_IsCollapsedByDefault_AndItsHeaderExpandsIt()
    {
        await Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo", null, false, false, false, null, "main", null));
        await Services.GetRequiredService<ITodoTaskStore>().CreateRunAsync("owner/repo",
            new NewTodoTask(TodoTaskKind.AnalyzeRepository, "Analyze the repository", "{}"), "octocat", 3, _cancellationToken);

        Render<MudPopoverProvider>();
        var cut = Render<BackgroundTasks>();

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find(".run-toggle").GetAttribute("aria-expanded")));
        Assert.False(cut.FindComponent<MudCollapse>().Instance.Expanded);

        await cut.InvokeAsync(() => cut.Find(".run-toggle").Click());

        Assert.Equal("true", cut.Find(".run-toggle").GetAttribute("aria-expanded"));
        Assert.True(cut.FindComponent<MudCollapse>().Instance.Expanded);
    }

    [Fact]
    public async Task Page_WithoutRuns_ExplainsHowToStartOne()
    {
        await Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo", null, false, false, false, null, "main", null));

        Render<MudPopoverProvider>();
        var cut = Render<BackgroundTasks>();

        cut.WaitForAssertion(() => Assert.Contains("No runs yet.", cut.Markup));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Generate from repository"));
    }
}
