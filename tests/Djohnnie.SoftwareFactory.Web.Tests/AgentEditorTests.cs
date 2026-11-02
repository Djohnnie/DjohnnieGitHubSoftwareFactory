using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;
using Djohnnie.SoftwareFactory.Application.Features.AiModels;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
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

public class AgentEditorTests : BunitContext, IAsyncLifetime
{
    private readonly FakeGitHub _gitHub = new();
    private ScriptedChatClient? _chatClient;

    ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

    public AgentEditorTests()
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
    public async Task NewAgentFromTemplate_PrefillsTheFormAndCommitsTheTemplate()
    {
        await SelectRepositoryAsync();
        var template = await Services.GetRequiredService<AgentTemplateService>().SaveAsync(0, "reviewer", new AgentDefinition { Name = "Code Reviewer", Description = "Reviews code.", Prompt = "Review carefully.", Tools = ["read"], ToolsMode = AgentToolsMode.Selected }, Xunit.TestContext.Current.CancellationToken);
        Render<MudPopoverProvider>();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/agents/new?template={template.Id}");

        var cut = Render<AgentEditor>();
        cut.WaitForElement("textarea");

        Assert.Contains("Prefilled from the template", cut.Markup);
        Assert.Equal("Code Reviewer", cut.FindAll("input")[0].GetAttribute("value"));
        Assert.Equal("reviewer", cut.FindAll("input")[1].GetAttribute("value"));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save & commit")).Click());

        cut.WaitForAssertion(() => Assert.EndsWith("/agents", Services.GetRequiredService<NavigationManager>().Uri), TimeSpan.FromSeconds(2));
        var put = Assert.Single(_gitHub.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal("repos/owner/repo/contents/.github/agents/reviewer.agent.md", put.Path);
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal(template.Content, Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!)));
    }

    [Fact]
    public async Task Assistant_PrefillsTheFormFromThePrompt()
    {
        await SelectRepositoryAsync();
        await Services.GetRequiredService<AiModelService>().SaveAsync(new AiModel { Name = "GPT-5", ModelId = "gpt-5", InputPrice = 1m, OutputPrice = 8m }, Xunit.TestContext.Current.CancellationToken);
        _chatClient = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("update_agent_form", new Dictionary<string, object?>
            {
                ["name"] = "Security Reviewer",
                ["description"] = "Reviews code for security issues.",
                ["instructions"] = "You are a security reviewer.",
                ["model"] = "GPT-5",
                ["tools"] = new[] { "read", "search" }
            }),
            ScriptedChatClient.Text("I filled in the form. Review it and click Save & commit."));
        Render<MudPopoverProvider>();
        var cut = Render<AgentEditor>();
        cut.WaitForElement("textarea");

        var chatInput = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Describe the agent you need").Find("input");
        chatInput.Input("An agent that reviews for security issues");
        await cut.InvokeAsync(() => chatInput.KeyDown("Enter"));

        cut.WaitForAssertion(() => Assert.Contains("I filled in the form.", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Contains("Updated: name, file name, description, instructions, model, tools.",
            _chatClient.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().Single().Result?.ToString());

        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save & commit")).Click());

        cut.WaitForAssertion(() => Assert.Single(_gitHub.Requests, r => r.Method == HttpMethod.Put), TimeSpan.FromSeconds(2));
        var put = _gitHub.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.Equal("repos/owner/repo/contents/.github/agents/security-reviewer.agent.md", put.Path);
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal(
            "---\nname: Security Reviewer\ndescription: Reviews code for security issues.\nmodel: gpt-5\ntools: ['read', 'search']\n---\n\nYou are a security reviewer.\n",
            Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!)));
    }

    private Task SelectRepositoryAsync() =>
        Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));

    [Fact]
    public async Task Save_NewAgent_CommitsAgentFileAndReturnsToList()
    {
        await Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", "owner/repo", new GitHubOwner("owner", null), "https://github.com/owner/repo",
            null, false, false, false, null, "main", null));
        await Services.GetRequiredService<AiModelService>().SaveAsync(new AiModel
        {
            Name = "Claude Sonnet 4.6",
            ModelId = "claude-sonnet-4.6",
            Provider = "Anthropic",
            InputPrice = 3m,
            OutputPrice = 15m
        }, Xunit.TestContext.Current.CancellationToken);
        Render<MudPopoverProvider>();
        var cut = Render<AgentEditor>();
        cut.WaitForElement("textarea");

        cut.FindAll("input")[0].Input("Code Reviewer");
        cut.WaitForAssertion(() => Assert.Equal("code-reviewer", cut.FindAll("input")[1].GetAttribute("value")), TimeSpan.FromSeconds(2));
        cut.FindAll("textarea")[0].Change("Reviews pull requests");
        var modelSelect = cut.FindComponents<MudSelect<string>>().Single(s => s.Instance.Label == "Model");
        await cut.InvokeAsync(() => modelSelect.Instance.ValueChanged.InvokeAsync("claude-sonnet-4.6"));
        cut.WaitForAssertion(() => Assert.Contains("$3.00 in / $15.00 out per 1M tokens", cut.Markup));
        Assert.Equal("Claude Sonnet 4.6 — $3.00 in / $15.00 out", modelSelect.Instance.ToStringFunc!("claude-sonnet-4.6"));
        await cut.InvokeAsync(() => cut.FindComponent<MarkdownEditor>().Instance.ValueChanged.InvokeAsync("Review carefully."));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Contains("Save")).Click());

        cut.WaitForAssertion(() => Assert.EndsWith("/agents", Services.GetRequiredService<NavigationManager>().Uri), TimeSpan.FromSeconds(2));
        var put = Assert.Single(_gitHub.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal("repos/owner/repo/contents/.github/agents/code-reviewer.agent.md", put.Path);
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal("Create custom agent Code Reviewer", body.RootElement.GetProperty("message").GetString());
        Assert.False(body.RootElement.TryGetProperty("sha", out _));
        Assert.Equal(
            "---\nname: Code Reviewer\ndescription: Reviews pull requests\nmodel: claude-sonnet-4.6\n---\n\nReview carefully.\n",
            Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!)));
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath.TrimStart('/'), body));

            return request.Method == HttpMethod.Put
                ? new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"content":{"sha":"blob"},"commit":{"sha":"1234567890","html_url":"https://github.com/c"}}""", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}