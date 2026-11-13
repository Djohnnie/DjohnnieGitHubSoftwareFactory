using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Skills;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class SkillTests : BunitContext
{
    private const string Repository = "owner/repo";
    private readonly FakeGitHub _gitHub = new();

    public SkillTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddDataProtection();
        Services.AddLogging();
        AddAuthorization().SetAuthorized("Djohnnie").SetClaims(new Claim(GitHubClaims.AccessToken, "token"));
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<RepositoryContext>();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton(TimeProvider.System);
        Services.AddApplication();
        Services.AddSingleton(new AssistantAgentFactory(null, NullLoggerFactory.Instance));
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    private SkillService Skills => Services.GetRequiredService<SkillService>();

    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public void Serialize_WritesTheFrontmatterAndInstructions()
    {
        var content = SkillService.Render(new Skill
        {
            Name = "release-notes",
            Description = "Writes release notes: use it when preparing a release.",
            License = "MIT",
            AllowedTools = "shell(git:*)",
            Instructions = "\r\n# Release notes\r\n\r\nList the merged pull requests.\r\n"
        });

        Assert.Equal(
            "---\nname: release-notes\ndescription: 'Writes release notes: use it when preparing a release.'\nlicense: MIT\nallowed-tools: shell(git:*)\n---\n\n# Release notes\n\nList the merged pull requests.\n",
            content);
    }

    [Theory]
    [InlineData("pdf-processing", true)]
    [InlineData("a1", true)]
    [InlineData("PDF", false)]
    [InlineData("-pdf", false)]
    [InlineData("pdf-", false)]
    [InlineData("pdf--tools", false)]
    [InlineData("pdf_tools", false)]
    [InlineData("", false)]
    public void IsValidName_FollowsTheSkillNameRules(string name, bool valid)
    {
        Assert.Equal(valid, Skill.IsValidName(name));
    }

    [Fact]
    public void Slugify_TurnsTextIntoAValidName()
    {
        Assert.Equal("review-pull-requests", Skill.Slugify("  Review  Pull_Requests! "));
        Assert.True(Skill.IsValidName(Skill.Slugify(new string('a', 70))));
    }

    [Fact]
    public async Task Save_TrimsPropertiesAndRejectsDuplicateNames()
    {
        var skill = await Skills.SaveAsync(new Skill { Name = "reviewer", Description = "  Reviews code.  ", License = "  ", Instructions = "Review.\r\n" }, Token);

        Assert.Equal("Reviews code.", skill.Description);
        Assert.Null(skill.License);
        Assert.Equal("Review.", skill.Instructions);
        var duplicate = await Assert.ThrowsAsync<DomainRuleException>(() =>
            Skills.SaveAsync(new Skill { Name = "reviewer", Description = "Other." }, Token));
        Assert.Contains("already named 'reviewer'", duplicate.Message);
        await Assert.ThrowsAsync<ValidationFailedException>(() => Skills.SaveAsync(new Skill { Name = "Not Valid", Description = "x" }, Token));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Skills.SaveAsync(new Skill { Name = "no-description" }, Token));

        var updated = await Skills.SaveAsync(skill with { Description = "Reviews code carefully." }, Token);
        Assert.Equal(skill.Id, updated.Id);
        Assert.Equal("Reviews code carefully.", Assert.Single(await Skills.GetAllAsync(Token)).Description);
    }

    [Fact]
    public async Task GetRepositorySkills_ComparesEachSkillWithTheRepositoryFile()
    {
        var enabled = await Skills.SaveAsync(new Skill { Name = "enabled", Description = "Enabled." }, Token);
        var outdated = await Skills.SaveAsync(new Skill { Name = "outdated", Description = "Outdated." }, Token);
        await Skills.SaveAsync(new Skill { Name = "disabled", Description = "Disabled." }, Token);
        _gitHub.Files[Skill.GetPath("enabled")] = SkillService.Render(enabled).Replace("\n", "\r\n");
        _gitHub.Files[Skill.GetPath("outdated")] = SkillService.Render(outdated with { Instructions = "Old." });
        _gitHub.Files[".github/skills/hand-made/SKILL.md"] = "---\nname: hand-made\n---\n";

        var result = await Skills.GetRepositorySkillsAsync(Repository, Token);

        Assert.Equal(
            [("disabled", SkillRepositoryStatus.Disabled), ("enabled", SkillRepositoryStatus.Enabled), ("outdated", SkillRepositoryStatus.Outdated)],
            result.Skills.Select(s => (s.Skill.Name, s.Status)));
        Assert.Equal(["hand-made"], result.UnmanagedSkills);
        // Only the files of skills that have a directory are read.
        Assert.DoesNotContain(_gitHub.Requests, r => r.Path.Contains("/disabled/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enable_CommitsTheSkillFileOnlyWhenItDiffers()
    {
        var skill = await Skills.SaveAsync(new Skill { Name = "reviewer", Description = "Reviews code.", Instructions = "Review carefully." }, Token);

        var created = await Skills.EnableAsync(Repository, skill.Id, Token);

        Assert.True(created.Changed);
        Assert.Equal("1234567890", created.CommitSha);
        var put = Assert.Single(_gitHub.Requests, r => r.Method == "PUT");
        Assert.Equal("/repos/owner/repo/contents/.github/skills/reviewer/SKILL.md", put.Path);
        using (var body = JsonDocument.Parse(put.Body!))
        {
            Assert.Equal("Enable skill reviewer", body.RootElement.GetProperty("message").GetString());
            Assert.Equal("---\nname: reviewer\ndescription: Reviews code.\n---\n\nReview carefully.\n",
                Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!)));
            Assert.False(body.RootElement.TryGetProperty("sha", out _));
        }

        Assert.False((await Skills.EnableAsync(Repository, skill.Id, Token)).Changed);
        Assert.Single(_gitHub.Requests, r => r.Method == "PUT");

        await Skills.SaveAsync(skill with { Instructions = "Review very carefully." }, Token);
        Assert.True((await Skills.EnableAsync(Repository, skill.Id, Token)).Changed);
        using var update = JsonDocument.Parse(_gitHub.Requests.Last(r => r.Method == "PUT").Body!);
        Assert.Equal("Update skill reviewer", update.RootElement.GetProperty("message").GetString());
        Assert.Equal("sha-.github/skills/reviewer/SKILL.md", update.RootElement.GetProperty("sha").GetString());
    }

    [Fact]
    public async Task Disable_DeletesTheSkillFileWhenItExists()
    {
        var skill = await Skills.SaveAsync(new Skill { Name = "reviewer", Description = "Reviews code." }, Token);

        Assert.False((await Skills.DisableAsync(Repository, skill.Id, Token)).Changed);
        Assert.DoesNotContain(_gitHub.Requests, r => r.Method == "DELETE");

        _gitHub.Files[Skill.GetPath("reviewer")] = "anything";
        var result = await Skills.DisableAsync(Repository, skill.Id, Token);

        Assert.True(result.Changed);
        var delete = Assert.Single(_gitHub.Requests, r => r.Method == "DELETE");
        Assert.Equal("/repos/owner/repo/contents/.github/skills/reviewer/SKILL.md", delete.Path);
        using var body = JsonDocument.Parse(delete.Body!);
        Assert.Equal("Disable skill reviewer", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("sha-.github/skills/reviewer/SKILL.md", body.RootElement.GetProperty("sha").GetString());
        Assert.Empty(_gitHub.Files);
    }

    [Fact]
    public async Task SkillsPage_SwitchesCommitAndDeleteTheSkillFile()
    {
        await SelectRepositoryAsync();
        await Skills.SaveAsync(new Skill { Name = "reviewer", Description = "Reviews code." }, Token);
        Render<MudPopoverProvider>();
        var cut = Render<Components.Pages.Skills>();
        cut.WaitForAssertion(() => Assert.Contains("Disabled", cut.Find("tbody").TextContent));

        await cut.InvokeAsync(() => cut.Find("tbody input[type=checkbox]").Change(true));

        cut.WaitForAssertion(() => Assert.Contains("Enabled", cut.Find("tbody").TextContent));
        Assert.True(_gitHub.Files.ContainsKey(Skill.GetPath("reviewer")));

        await cut.InvokeAsync(() => cut.Find("tbody input[type=checkbox]").Change(false));

        cut.WaitForAssertion(() => Assert.Contains("Disabled", cut.Find("tbody").TextContent));
        Assert.Empty(_gitHub.Files);
        Assert.Single(_gitHub.Requests, r => r.Method == "DELETE");
    }

    [Fact]
    public async Task SkillEditor_SavesANewSkillWithoutARepository()
    {
        Render<MudPopoverProvider>();
        var cut = Render<SkillEditor>();
        cut.WaitForElement("textarea");

        cut.FindAll("input")[0].Change("PR Reviewer");
        cut.FindAll("textarea")[0].Change("Reviews pull requests.");
        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("Review every file."));
        cut.WaitForAssertion(() => Assert.Contains("name: pr-reviewer", cut.Find("pre.skill-preview").TextContent));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save skill")).Click());

        cut.WaitForAssertion(() => Assert.EndsWith("/skills", Services.GetRequiredService<NavigationManager>().Uri), TimeSpan.FromSeconds(2));
        var skill = Assert.Single(await Skills.GetAllAsync(Token));
        Assert.Equal("pr-reviewer", skill.Name);
        Assert.Equal("Reviews pull requests.", skill.Description);
        Assert.Equal("Review every file.", skill.Instructions);
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", Repository, new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));
    }

    private sealed record Request(string Method, string Path, string? Body);

    /// <summary>The contents API of one repository: lists <c>.github/skills</c> and serves, creates and deletes files.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private const string Prefix = "/repos/owner/repo/contents/";

        public List<Request> Requests { get; } = [];

        /// <summary>The repository's files by path.</summary>
        public Dictionary<string, string> Files { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new Request(request.Method.Method, path, body));
            if (!path.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var filePath = Uri.UnescapeDataString(path[Prefix.Length..]);
            if (request.Method == HttpMethod.Put)
            {
                using var json = JsonDocument.Parse(body!);
                Files[filePath] = Encoding.UTF8.GetString(Convert.FromBase64String(json.RootElement.GetProperty("content").GetString()!));
                return Json(HttpStatusCode.OK, """{"content":{"sha":"blob"},"commit":{"sha":"1234567890","html_url":"https://github.com/c"}}""");
            }

            if (request.Method == HttpMethod.Delete)
            {
                Files.Remove(filePath);
                return Json(HttpStatusCode.OK, """{"content":null,"commit":{"sha":"abcdef1234","html_url":"https://github.com/d"}}""");
            }

            if (filePath == Skill.Directory)
            {
                var directories = Files.Keys.Where(p => p.StartsWith(Skill.Directory + "/", StringComparison.Ordinal))
                    .Select(p => p.Split('/')[2]).Distinct()
                    .Select(name => new Dictionary<string, string>
                    {
                        ["name"] = name, ["path"] = Skill.GetDirectory(name), ["sha"] = "tree", ["type"] = "dir"
                    });
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(directories));
            }

            return Files.TryGetValue(filePath, out var content)
                ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["type"] = "file",
                    ["path"] = filePath,
                    ["sha"] = $"sha-{filePath}",
                    ["encoding"] = "base64",
                    ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
                }))
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
        {
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
