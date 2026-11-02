using System.Net;
using System.Net.Http.Json;
using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class RepositoryPickerTests : BunitContext, IAsyncLifetime
{
    private readonly DelayedGitHub _gitHub = new();
    private static readonly GitHubRepository Repository = new(
        1, "recent", "owner/recent", new GitHubOwner("owner", null),
        "https://github.com/owner/recent", "Recent repository", false, false, false, "C#", "main", null);

    ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

    public RepositoryPickerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddDataProtection();
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<RepositoryContext>();
        Services.AddApplication();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(options));
        Services.AddSingleton(new GitHubApiClient(
            new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") }, new TokenProvider()));
    }

    [Fact]
    public async Task RecentRepository_CanBeSelectedWhileFullListIsStillLoading()
    {
        await Services.GetRequiredService<RepositoryHistoryService>().RecordAccessAsync(
            Repository.FullName, Xunit.TestContext.Current.CancellationToken);
        GitHubRepository? selected = null;
        var cut = Render<RepositoryPicker>(parameters => parameters
            .Add(component => component.OnSelected, repository => selected = repository));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Recently accessed", cut.Markup);
            Assert.NotNull(cut.Find("[aria-label='Loading repositories']"));
            Assert.Empty(cut.FindAll("table"));
        });
        await cut.InvokeAsync(() => cut.FindAll("button").Single(button => button.TextContent.Contains(Repository.FullName)).Click());

        cut.WaitForAssertion(() => Assert.Equal(Repository, selected));
        Assert.False(_gitHub.ListResponse.Task.IsCompleted);
        Assert.Contains("/repos/owner/recent", _gitHub.Requests);
    }

    [Fact]
    public async Task LoadedList_ReplacesSpinnerAndRemainsSearchable()
    {
        var cut = Render<RepositoryPicker>();
        Assert.NotNull(cut.Find("[aria-label='Loading repositories']"));

        _gitHub.ListResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { Repository, Repository with { Id = 2, Name = "other", FullName = "owner/other" } })
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[aria-label='Loading repositories']"));
            Assert.Contains("owner/recent", cut.Markup);
            Assert.Contains("owner/other", cut.Markup);
        });
        await cut.InvokeAsync(() => cut.Find("input").Input("owner/recent"));

        cut.WaitForAssertion(() => Assert.DoesNotContain("owner/other", cut.Markup));
    }

    [Fact]
    public async Task InaccessibleRecentRepository_ShowsErrorAndDoesNotSelectIt()
    {
        await Services.GetRequiredService<RepositoryHistoryService>().RecordAccessAsync(
            Repository.FullName, Xunit.TestContext.Current.CancellationToken);
        _gitHub.RecentStatus = HttpStatusCode.NotFound;
        GitHubRepository? selected = null;
        var cut = Render<RepositoryPicker>(parameters => parameters
            .Add(component => component.OnSelected, repository => selected = repository));

        await cut.InvokeAsync(() => cut.FindAll("button").Single(button => button.TextContent.Contains(Repository.FullName)).Click());

        cut.WaitForAssertion(() => Assert.Contains("Could not select repository", cut.Markup));
        Assert.Null(selected);
    }

    [Fact]
    public async Task SelectingRepository_RecordsAccessInDatabase()
    {
        var context = Services.GetRequiredService<RepositoryContext>();

        await context.SelectAsync(Repository);

        var recent = Assert.Single(await Services.GetRequiredService<RepositoryHistoryService>()
            .GetRecentAsync(Xunit.TestContext.Current.CancellationToken));
        Assert.Equal(Repository.FullName, recent.FullName);
        Assert.Equal(Repository, context.Current);
    }

    [Fact]
    public async Task RestoringSelectedRepository_RefreshesAccessTime()
    {
        var context = Services.GetRequiredService<RepositoryContext>();
        await context.SelectAsync(Repository);
        var encryptedSelection = (string)JSInterop.Invocations["localStorage.setItem"].Single().Arguments[1]!;
        JSInterop.Setup<string>("localStorage.getItem", "dghsf.repository").SetResult(encryptedSelection);
        var previousAccess = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var db = Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            var stored = await db.RepositoryAccesses.SingleAsync(Xunit.TestContext.Current.CancellationToken);
            stored.LastAccessedAt = previousAccess;
            await db.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);
        }
        var restored = new RepositoryContext(
            Services.GetRequiredService<ProtectedLocalStorage>(),
            Services.GetRequiredService<ILogger<RepositoryContext>>(),
            Services.GetRequiredService<RepositoryHistoryService>());

        await restored.LoadAsync();

        Assert.Equal(Repository, restored.Current);
        var recent = Assert.Single(await Services.GetRequiredService<RepositoryHistoryService>()
            .GetRecentAsync(Xunit.TestContext.Current.CancellationToken));
        Assert.True(recent.LastAccessedAt > previousAccess);
    }

    [Fact]
    public async Task FullListFailure_LeavesRecentRepositoriesSelectable()
    {
        await Services.GetRequiredService<RepositoryHistoryService>().RecordAccessAsync(
            Repository.FullName, Xunit.TestContext.Current.CancellationToken);
        GitHubRepository? selected = null;
        var cut = Render<RepositoryPicker>(parameters => parameters
            .Add(component => component.OnSelected, repository => selected = repository));
        _gitHub.ListResponse.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Could not load repositories", cut.Markup);
            Assert.Empty(cut.FindAll("[aria-label='Loading repositories']"));
        });
        await cut.InvokeAsync(() => cut.FindAll("button").Single(button => button.TextContent.Contains(Repository.FullName)).Click());

        cut.WaitForAssertion(() => Assert.Equal(Repository, selected));
    }

    private sealed class TokenProvider : IGitHubTokenProvider
    {
        public Task<string> GetAccessTokenAsync() => Task.FromResult("token");
    }

    private sealed class DelayedGitHub : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> ListResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Requests { get; } = [];
        public HttpStatusCode RecentStatus { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath == "/user/repos"
                ? ListResponse.Task.WaitAsync(cancellationToken)
                : Task.FromResult(new HttpResponseMessage(RecentStatus) { Content = JsonContent.Create(Repository) });
        }
    }
}
