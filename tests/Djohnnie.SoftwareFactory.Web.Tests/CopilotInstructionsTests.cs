using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class CopilotInstructionsTests : BunitContext
{
    private const string FilePath = ".github/copilot-instructions.md";
    private readonly FakeGitHub _gitHub = new();
    private ScriptedChatClient? _chatClient;

    public CopilotInstructionsTests()
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
        Services.AddSingleton(_ => new AssistantAgentFactory(_chatClient, NullLoggerFactory.Instance));
        Services.AddScoped(sp => new GitHubApiClient(
            new HttpClient(_gitHub) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubTokenProvider(sp.GetRequiredService<AuthenticationStateProvider>())));
    }

    [Fact]
    public async Task Copilot_ExploresTheRepository_AndSaveCommitsTheUpdatedFile()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        _gitHub.Files[FilePath] = "# Old instructions\r\n";
        _gitHub.Files["README.md"] = "A tiny calculator written in Rust.";
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("list_repository_files", new Dictionary<string, object?> { ["path"] = "" }),
            ScriptedChatClient.ToolCall("read_repository_file", new Dictionary<string, object?> { ["path"] = "./README.md" }),
            ScriptedChatClient.ToolCall("update_instructions_file", new Dictionary<string, object?> { ["content"] = "# Copilot instructions\r\n\r\n- Build with `cargo build`." }),
            ScriptedChatClient.Text("I rewrote the instructions. Review them and click Save & commit."));
        Render<MudPopoverProvider>();
        var cut = Render<CopilotInstructions>();
        cut.WaitForAssertion(() => Assert.Equal("# Old instructions", cut.FindComponent<MarkdownEditor>().Instance.Value));
        Assert.DoesNotContain("Saving creates it", cut.Markup);

        var chatInput = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Tell me what Copilot should know about this repository").Find("input");
        chatInput.Input("Write the instructions");
        await cut.InvokeAsync(() => chatInput.KeyDown("Enter"));

        cut.WaitForAssertion(() => Assert.Contains("I rewrote the instructions.", cut.Markup), TimeSpan.FromSeconds(5));
        var results = _chatClient.Requests[3].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString()).ToList();
        Assert.Equal(".github/\nREADME.md", results[0]);
        Assert.Equal("A tiny calculator written in Rust.", results[1]);
        Assert.Equal("Updated: instructions file.", results[2]);
        Assert.Equal("# Copilot instructions\n\n- Build with `cargo build`.\n", cut.FindComponent<MarkdownEditor>().Instance.Value);
        Assert.Empty(_gitHub.Requests.Where(r => r.Method == HttpMethod.Put));

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save & commit")).Click());

        cut.WaitForAssertion(() => Assert.Single(_gitHub.Requests, r => r.Method == HttpMethod.Put), TimeSpan.FromSeconds(2));
        var put = _gitHub.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.Equal($"repos/owner/repo/contents/{FilePath}", put.Path);
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal("Update Copilot instructions", body.RootElement.GetProperty("message").GetString());
        Assert.Equal($"sha-{FilePath}", body.RootElement.GetProperty("sha").GetString());
        Assert.Equal("# Copilot instructions\n\n- Build with `cargo build`.\n",
            Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!)));
    }

    [Fact]
    public async Task MissingFile_IsCreatedOnSave_AndTheNextSaveUpdatesIt()
    {
        await SelectRepositoryAsync();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.ChatSuggestions);
        Render<MudPopoverProvider>();
        var cut = Render<CopilotInstructions>();
        cut.WaitForAssertion(() => Assert.Contains("Saving creates it", cut.Markup));

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save & commit")).Click());
        Assert.Empty(_gitHub.Requests.Where(r => r.Method == HttpMethod.Put));

        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("# Instructions\n\nUse tabs."));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save & commit")).Click());

        cut.WaitForAssertion(() => Assert.Single(_gitHub.Requests, r => r.Method == HttpMethod.Put), TimeSpan.FromSeconds(2));
        using (var body = JsonDocument.Parse(_gitHub.Requests.Single(r => r.Method == HttpMethod.Put).Body!))
        {
            Assert.Equal("Add Copilot instructions", body.RootElement.GetProperty("message").GetString());
            Assert.False(body.RootElement.TryGetProperty("sha", out _));
        }
        cut.WaitForAssertion(() => Assert.DoesNotContain("Saving creates it", cut.Markup));

        // Saving again without changes doesn't commit; after a change the new blob sha is used.
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save & commit")).Click());
        Assert.Single(_gitHub.Requests, r => r.Method == HttpMethod.Put);
        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("# Instructions\n\nUse spaces."));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save & commit")).Click());

        cut.WaitForAssertion(() => Assert.Equal(2, _gitHub.Requests.Count(r => r.Method == HttpMethod.Put)), TimeSpan.FromSeconds(2));
        using var second = JsonDocument.Parse(_gitHub.Requests.Last(r => r.Method == HttpMethod.Put).Body!);
        Assert.Equal("Update Copilot instructions", second.RootElement.GetProperty("message").GetString());
        Assert.Equal("blob", second.RootElement.GetProperty("sha").GetString());
    }

    [Fact]
    public void Defaults_IncludeTheCopilotInstructionsCopilot()
    {
        var copilot = InternalAgentDefaults.Find(InternalAgentKeys.CopilotInstructionsCopilot)!;

        Assert.Equal(InternalAgentKind.Copilot, copilot.Kind);
        Assert.Equal("Copilot Instructions Copilot", copilot.Name);
        Assert.Contains("`update_instructions_file`", copilot.Instructions);
        Assert.Contains("`read_repository_file`", copilot.Instructions);
        Assert.Equal("Copilot instructions editor", InternalAgents.UsedOn(copilot));
    }

    private Task SelectRepositoryAsync()
    {
        return Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        private const string ContentsPrefix = "repos/owner/repo/contents/";

        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

        /// <summary>The files on the default branch by path.</summary>
        public Dictionary<string, string> Files { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath.TrimStart('/'));
            Requests.Add((request.Method, path, body));

            if (request.Method == HttpMethod.Put)
            {
                return Json(HttpStatusCode.OK, """{"content":{"sha":"blob"},"commit":{"sha":"1234567890","html_url":"https://github.com/c"}}""");
            }
            if (path == ContentsPrefix)
            {
                return Json(HttpStatusCode.OK, """
                    [{"name":"README.md","path":"README.md","sha":"a","type":"file"},{"name":".github","path":".github","sha":"b","type":"dir"}]
                    """);
            }
            if (path.StartsWith(ContentsPrefix, StringComparison.Ordinal) && Files.TryGetValue(path[ContentsPrefix.Length..], out var content))
            {
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["path"] = path[ContentsPrefix.Length..],
                    ["sha"] = $"sha-{path[ContentsPrefix.Length..]}",
                    ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
                }));
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
        {
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
