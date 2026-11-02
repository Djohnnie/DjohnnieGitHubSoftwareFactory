using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Contracts.Agents;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class AgentFileSerializerTests
{
    [Fact]
    public void Serialize_NewAgent_WritesFrontmatterAndPrompt()
    {
        var agent = new AgentDefinition
        {
            Name = "Planner",
            Description = "Plans work: epics, features and stories",
            Target = "github-copilot",
            ToolsMode = AgentToolsMode.Selected,
            Tools = ["read", "search", "github/*"],
            Prompt = "You are a planner.\r\n\r\nBe brief."
        };

        var content = AgentFileSerializer.Serialize(agent);

        Assert.Equal(
            """
            ---
            name: Planner
            description: 'Plans work: epics, features and stories'
            target: github-copilot
            tools: ['read', 'search', 'github/*']
            ---

            You are a planner.

            Be brief.

            """.ReplaceLineEndings("\n"),
            content);
    }

    [Fact]
    public void RoundTrip_KeepsUnknownKeysAndOrder_AndUpdatesKnownKeys()
    {
        const string original = """
            ---
            description: Old description
            infer: false
            name: Reviewer
            x-custom:
              nested: [1, 2]
            tools: read, edit
            handoffs:
              - label: Implement
                agent: implementer
                prompt: Go
                model: gpt-5
            mcp-servers:
              custom-mcp:
                type: local
                command: some-command
            ---
            Review code.
            """;

        var agent = AgentFileSerializer.Parse(original);
        Assert.Equal(["infer", "x-custom"], agent.PreservedKeys);
        Assert.Equal(AgentToolsMode.Selected, agent.ToolsMode);
        Assert.Equal(["read", "edit"], agent.Tools);

        agent.Description = "New description";
        agent.Model = "claude-sonnet-4.5";
        agent.Handoffs[0].Send = true;

        var content = AgentFileSerializer.Serialize(agent);

        Assert.Equal(
            """
            ---
            description: New description
            infer: false
            name: Reviewer
            x-custom:
              nested: [1, 2]
            tools: ['read', 'edit']
            handoffs:
            - label: Implement
              agent: implementer
              prompt: Go
              model: gpt-5
              send: true
            mcp-servers:
              custom-mcp:
                type: local
                command: some-command
            model: claude-sonnet-4.5
            ---

            Review code.

            """.ReplaceLineEndings("\n"),
            content);
    }

    [Theory]
    [InlineData("tools: []", AgentToolsMode.None)]
    [InlineData("tools: ['*']", AgentToolsMode.All)]
    [InlineData("name: x", AgentToolsMode.All)]
    public void Parse_ToolsMode(string line, AgentToolsMode expected)
    {
        var agent = AgentFileSerializer.Parse($"---\ndescription: d\n{line}\n---\n");

        Assert.Equal(expected, agent.ToolsMode);
    }

    [Fact]
    public void Serialize_QuotesValuesThatWouldNotReadBackAsStrings()
    {
        var agent = new AgentDefinition { Name = "true", Description = "line one\nline two", Model = "1.5" };

        var parsed = AgentFileSerializer.Parse(AgentFileSerializer.Serialize(agent));

        Assert.Equal("true", parsed.Name);
        Assert.Equal("line one\nline two", parsed.Description);
        Assert.Equal("1.5", parsed.Model);
    }

    [Fact]
    public void Serialize_KeepsExplicitDefaultsThatWereInTheFile_AndOmitsThemOtherwise()
    {
        var existing = AgentFileSerializer.Parse("---\ndescription: d\nuser-invocable: true\ntools: ['*']\n---\n");
        var created = new AgentDefinition { Description = "d" };

        Assert.Equal("---\ndescription: d\nuser-invocable: true\ntools: ['*']\n---\n", AgentFileSerializer.Serialize(existing));
        Assert.Equal("---\ndescription: d\n---\n", AgentFileSerializer.Serialize(created));
    }

    [Fact]
    public void Parse_InvalidFrontmatter_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => AgentFileSerializer.Parse("---\ndescription: [unclosed\n---\nbody"));
        Assert.Throws<FormatException>(() => AgentFileSerializer.Parse("---\ndescription: never closed\nbody"));
    }

    [Fact]
    public void Serialize_InvalidMcpServers_ThrowsFormatException()
    {
        var agent = new AgentDefinition { Description = "d", McpServersYaml = "- not a mapping" };

        Assert.NotNull(AgentFileSerializer.ValidateMcpServers(agent.McpServersYaml));
        Assert.Throws<FormatException>(() => AgentFileSerializer.Serialize(agent));
    }
}