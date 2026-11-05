using System.Text.Json;
using Djohnnie.SoftwareFactory.Agents;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class RepositoryScopedFunctionTests
{
    [Fact]
    public async Task Invoke_OverridesOwnerAndRepo()
    {
        var inner = AIFunctionFactory.Create((string owner, string repo, int page) =>
        {
            return $"{owner}/{repo}/{page}";
        }, "list_commits");
        var scoped = new RepositoryScopedFunction(inner, "djohnnie/app");

        var result = await scoped.InvokeAsync(new AIFunctionArguments
        {
            ["owner"] = "someone-else",
            ["repo"] = "secret",
            ["page"] = 2
        }, TestContext.Current.CancellationToken);

        Assert.Equal("djohnnie/app/2", Text(result));
    }

    [Fact]
    public async Task Invoke_AddsOwnerAndRepo_WhenTheModelLeavesThemOut()
    {
        var inner = AIFunctionFactory.Create((string owner, string repo) =>
        {
            return $"{owner}/{repo}";
        }, "list_issues");
        var scoped = new RepositoryScopedFunction(inner, "djohnnie/app");

        var result = await scoped.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal("djohnnie/app", Text(result));
    }

    [Fact]
    public void JsonSchema_HidesOwnerAndRepo()
    {
        var inner = AIFunctionFactory.Create((string owner, string repo, string sha) =>
        {
            return sha;
        }, "get_commit");
        var scoped = new RepositoryScopedFunction(inner, "djohnnie/app");

        var properties = scoped.JsonSchema.GetProperty("properties");
        Assert.False(properties.TryGetProperty("owner", out _));
        Assert.False(properties.TryGetProperty("repo", out _));
        Assert.True(properties.TryGetProperty("sha", out _));
        if (scoped.JsonSchema.TryGetProperty("required", out var required))
        {
            Assert.DoesNotContain(required.EnumerateArray(), r => r.GetString() is "owner" or "repo");
        }
        Assert.Equal("get_commit", scoped.Name);
    }

    [Fact]
    public async Task Invoke_SearchQuery_IsPinnedToTheRepository()
    {
        var inner = AIFunctionFactory.Create((string query) =>
        {
            return query;
        }, "search_code");
        var scoped = new RepositoryScopedFunction(inner, "djohnnie/app");

        var result = await scoped.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = JsonSerializer.SerializeToElement("HttpClient repo:other/secret org:acme -user:x language:csharp")
        }, TestContext.Current.CancellationToken);

        Assert.Equal("repo:djohnnie/app HttpClient language:csharp", Text(result));
    }

    [Theory]
    [InlineData(null, "repo:djohnnie/app")]
    [InlineData("", "repo:djohnnie/app")]
    [InlineData("REPO:\"other/x\" is:open", "repo:djohnnie/app is:open")]
    [InlineData("owner:acme  bug", "repo:djohnnie/app bug")]
    [InlineData("label:repo:fix", "repo:djohnnie/app label:repo:fix")]
    public void ScopeQuery_RemovesScopeQualifiers(string? query, string expected)
    {
        var scoped = new RepositoryScopedFunction(AIFunctionFactory.Create((string query) =>
        {
            return query;
        }, "search_issues"), "djohnnie/app");

        Assert.Equal(expected, scoped.ScopeQuery(query));
    }

    [Theory]
    [InlineData("djohnnie")]
    [InlineData("/app")]
    [InlineData("djohnnie/")]
    public void Constructor_RejectsInvalidRepository(string repository)
    {
        var inner = AIFunctionFactory.Create(() =>
        {
            return 1;
        }, "x");

        Assert.Throws<ArgumentException>(() => new RepositoryScopedFunction(inner, repository));
    }

    [Theory]
    [InlineData("list_commits", """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"}}}""", true, true)]
    [InlineData("search_code", """{"type":"object","properties":{"query":{"type":"string"}}}""", true, true)]
    [InlineData("create_issue", """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"}}}""", false, false)]
    [InlineData("list_commits", """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"}}}""", null, false)]
    [InlineData("get_me", """{"type":"object","properties":{}}""", true, false)]
    [InlineData("list_issue_types", """{"type":"object","properties":{"owner":{"type":"string"}}}""", true, false)]
    [InlineData("get_file_contents", """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"}}}""", true, false)]
    [InlineData("search_repositories", """{"type":"object","properties":{"query":{"type":"string"}}}""", true, false)]
    public void IsAllowed_OnlyReadOnlyToolsThatCanBePinned(string name, string schema, bool? readOnly, bool expected)
    {
        var tool = new Tool
        {
            Name = name,
            InputSchema = JsonSerializer.Deserialize<JsonElement>(schema),
            Annotations = readOnly is null ? null : new ToolAnnotations { ReadOnlyHint = readOnly }
        };

        Assert.Equal(expected, GitHubMcpConnector.IsAllowed(tool));
    }

    private static string? Text(object? result)
    {
        return result is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : result?.ToString();
    }
}
