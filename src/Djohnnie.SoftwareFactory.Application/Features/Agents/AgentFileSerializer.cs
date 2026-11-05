using System.Text;
using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Contracts.Agents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

/// <summary>
/// Reads and writes custom agent files. Only the keys this app edits are rewritten; every other
/// frontmatter key (and the original key order) is preserved as-is.
/// </summary>
public static partial class AgentFileSerializer
{
    private const string Delimiter = "---";

    internal static readonly HashSet<string> KnownKeys =
    [
        "name", "description", "argument-hint", "target", "model", "tools",
        "disable-model-invocation", "user-invocable", "handoffs", "mcp-servers", "metadata"
    ];

    private static readonly HashSet<string> KnownHandoffKeys = ["label", "agent", "prompt", "send"];

    /// <summary>Parses an agent file. Throws <see cref="FormatException"/> when the frontmatter is not valid YAML.</summary>
    public static AgentDefinition Parse(string content)
    {
        var (frontmatter, body) = Split(content.Replace("\r\n", "\n"));
        var agent = new AgentDefinition { Prompt = body };

        if (string.IsNullOrWhiteSpace(frontmatter))
        {
            return agent;
        }

        var root = LoadNode(frontmatter) as YamlMappingNode
            ?? throw new FormatException("The frontmatter must be a YAML mapping (key: value pairs).");

        agent.OriginalFrontmatter = frontmatter;
        agent.PreservedKeys = root.Children.Keys.OfType<YamlScalarNode>()
            .Select(k => k.Value ?? string.Empty).Where(k => !KnownKeys.Contains(k)).ToList();
        agent.Name = GetString(root, "name");
        agent.Description = GetString(root, "description") ?? string.Empty;
        agent.ArgumentHint = GetString(root, "argument-hint");
        agent.Target = GetString(root, "target");
        agent.Model = GetString(root, "model");
        agent.DisableModelInvocation = GetBool(root, "disable-model-invocation") ?? false;
        agent.UserInvocable = GetBool(root, "user-invocable") ?? true;
        ReadTools(root, agent);

        if (Get(root, "handoffs") is YamlSequenceNode handoffs)
        {
            foreach (var item in handoffs.OfType<YamlMappingNode>())
            {
                agent.Handoffs.Add(new AgentHandoff
                {
                    Label = GetString(item, "label") ?? string.Empty,
                    Agent = GetString(item, "agent") ?? string.Empty,
                    Prompt = GetString(item, "prompt") ?? string.Empty,
                    Send = GetBool(item, "send") ?? false,
                    Original = Emit(item)
                });
            }
        }

        if (Get(root, "metadata") is YamlMappingNode metadata)
        {
            foreach (var (key, value) in metadata.Children)
            {
                agent.Metadata.Add(new AgentMetadataEntry
                {
                    Key = (key as YamlScalarNode)?.Value ?? string.Empty,
                    Value = (value as YamlScalarNode)?.Value ?? string.Empty
                });
            }
        }

        if (Get(root, "mcp-servers") is { } mcpServers)
        {
            agent.McpServersYaml = agent.OriginalMcpServersYaml = Emit(mcpServers);
        }

        return agent;
    }

    /// <summary>Writes an agent file. Throws <see cref="FormatException"/> when the MCP servers YAML is invalid.</summary>
    public static string Serialize(AgentDefinition agent)
    {
        var original = OriginalMapping(agent.OriginalFrontmatter);
        bool Had(string key)
        {
            return Get(original, key) is not null;
        }

        var root = Merge(original,
        [
            ("name", Str(agent.Name)),
            ("description", Str(agent.Description)),
            ("argument-hint", Str(agent.ArgumentHint)),
            ("target", Str(agent.Target)),
            ("model", Str(agent.Model)),
            ("tools", WriteTools(agent, Had("tools"))),
            ("disable-model-invocation", agent.DisableModelInvocation || Had("disable-model-invocation") ? Bool(agent.DisableModelInvocation) : null),
            ("user-invocable", !agent.UserInvocable || Had("user-invocable") ? Bool(agent.UserInvocable) : null),
            ("handoffs", WriteHandoffs(agent.Handoffs)),
            ("mcp-servers", WriteMcpServers(agent)),
            ("metadata", WriteMetadata(agent.Metadata))
        ]);

        var builder = new StringBuilder();
        builder.Append(Delimiter).Append('\n');
        if (root.Children.Count > 0)
        {
            builder.Append(Emit(root)).Append('\n');
        }

        builder.Append(Delimiter).Append('\n');

        var prompt = agent.Prompt.Replace("\r\n", "\n").Trim('\n');
        if (prompt.Length > 0)
        {
            builder.Append('\n').Append(prompt).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Validates raw MCP servers YAML; returns an error message or <c>null</c> when valid.</summary>
    public static string? ValidateMcpServers(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        try
        {
            return LoadNode(yaml) is YamlMappingNode ? null : "MCP servers must be a YAML mapping of server names to their configuration.";
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    private static (string? Frontmatter, string Body) Split(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd() != Delimiter)
        {
            return (null, text.Trim('\n'));
        }

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() == Delimiter)
            {
                var frontmatter = string.Join('\n', lines[1..i]);
                var body = string.Join('\n', lines[(i + 1)..]).Trim('\n');
                return (frontmatter, body);
            }
        }

        throw new FormatException("The frontmatter is not closed with '---'.");
    }

    private static void ReadTools(YamlMappingNode root, AgentDefinition agent)
    {
        var tools = Get(root, "tools") switch
        {
            YamlSequenceNode sequence => sequence.OfType<YamlScalarNode>().Select(s => s.Value ?? string.Empty).ToList(),
            YamlScalarNode { Value: { Length: > 0 } value } => value.Split(',').ToList(),
            YamlScalarNode or null => null,
            _ => throw new FormatException("'tools' must be a list or a comma separated string.")
        };

        tools = tools?.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        (agent.ToolsMode, agent.Tools) = tools switch
        {
            null => (AgentToolsMode.All, []),
            [] => (AgentToolsMode.None, []),
            _ when tools.Contains("*") => (AgentToolsMode.All, []),
            _ => (AgentToolsMode.Selected, tools)
        };
    }

    private static YamlNode? WriteTools(AgentDefinition agent, bool hadTools)
    {
        IEnumerable<string>? tools = agent.ToolsMode switch
        {
            AgentToolsMode.None => [],
            AgentToolsMode.Selected => agent.Tools.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(),
            _ => hadTools ? ["*"] : null
        };

        if (tools is null)
        {
            return null;
        }

        var sequence = new YamlSequenceNode { Style = YamlDotNet.Core.Events.SequenceStyle.Flow };
        foreach (var tool in tools)
        {
            sequence.Add(new YamlScalarNode(tool) { Style = ScalarStyle.SingleQuoted });
        }

        return sequence;
    }

    private static YamlNode? WriteHandoffs(List<AgentHandoff> handoffs)
    {
        if (handoffs.Count == 0)
        {
            return null;
        }

        var sequence = new YamlSequenceNode();
        foreach (var handoff in handoffs)
        {
            sequence.Add(Merge(OriginalMapping(handoff.Original),
            [
                ("label", Str(handoff.Label)),
                ("agent", Str(handoff.Agent)),
                ("prompt", Str(handoff.Prompt)),
                ("send", handoff.Send || Get(OriginalMapping(handoff.Original), "send") is not null ? Bool(handoff.Send) : null)
            ]));
        }

        return sequence;
    }

    private static YamlNode? WriteMetadata(List<AgentMetadataEntry> metadata)
    {
        var entries = metadata.Where(m => !string.IsNullOrWhiteSpace(m.Key)).ToList();
        if (entries.Count == 0)
        {
            return null;
        }

        var mapping = new YamlMappingNode();
        foreach (var entry in entries.DistinctBy(e => e.Key.Trim()))
        {
            mapping.Add(Str(entry.Key.Trim())!, Str(entry.Value) ?? new YamlScalarNode(string.Empty) { Style = ScalarStyle.DoubleQuoted });
        }

        return mapping;
    }

    private static YamlNode? WriteMcpServers(AgentDefinition agent)
    {
        var yaml = agent.McpServersYaml.Replace("\r\n", "\n").Trim();
        if (yaml.Length == 0)
        {
            return null;
        }

        if (yaml == agent.OriginalMcpServersYaml?.Trim() && Get(OriginalMapping(agent.OriginalFrontmatter), "mcp-servers") is { } unchanged)
        {
            return unchanged;
        }

        return LoadNode(yaml) as YamlMappingNode
            ?? throw new FormatException("MCP servers must be a YAML mapping of server names to their configuration.");
    }

    private static YamlMappingNode Merge(YamlMappingNode? original, IReadOnlyList<(string Key, YamlNode? Value)> known)
    {
        var result = new YamlMappingNode();
        var written = new HashSet<string>();

        foreach (var (key, value) in original?.Children ?? Enumerable.Empty<KeyValuePair<YamlNode, YamlNode>>())
        {
            var name = (key as YamlScalarNode)?.Value;
            var match = known.FirstOrDefault(k => k.Key == name);

            if (name is null || match.Key is null)
            {
                result.Add(key, value);
            }
            else if (written.Add(name) && match.Value is not null)
            {
                result.Add(key, match.Value);
            }
        }

        foreach (var (key, value) in known)
        {
            if (value is not null && !written.Contains(key))
            {
                result.Add(key, value);
            }
        }

        return result;
    }

    private static YamlMappingNode? OriginalMapping(string? yaml)
    {
        return yaml is null ? null : (YamlMappingNode)LoadNode(yaml);
    }

    private static YamlNode? Get(YamlMappingNode? mapping, string key)
    {
        return mapping is not null && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    }

    private static string? GetString(YamlMappingNode mapping, string key)
    {
        return (Get(mapping, key) as YamlScalarNode)?.Value;
    }

    private static bool? GetBool(YamlMappingNode mapping, string key)
    {
        return bool.TryParse(GetString(mapping, key), out var value) ? value : null;
    }

    private static YamlScalarNode? Str(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Replace("\r\n", "\n").Trim();
        var style = value.Contains('\n') ? ScalarStyle.Literal
            : LooksLikeNonString().IsMatch(value) ? ScalarStyle.DoubleQuoted
            : ScalarStyle.Any;

        return new YamlScalarNode(value) { Style = style };
    }

    private static YamlScalarNode Bool(bool value)
    {
        return new(value ? "true" : "false") { Style = ScalarStyle.Plain };
    }

    private static YamlNode LoadNode(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            return stream.Documents.Count > 0
                ? stream.Documents[0].RootNode
                : throw new FormatException("The YAML is empty.");
        }
        catch (YamlException ex)
        {
            throw new FormatException($"Invalid YAML (line {ex.Start.Line}, column {ex.Start.Column}): {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    private static string Emit(YamlNode node)
    {
        using var writer = new StringWriter { NewLine = "\n" };
        var emitter = new Emitter(writer, EmitterSettings.Default.WithBestWidth(int.MaxValue));
        new YamlStream(new YamlDocument(node)).Save(emitter, assignAnchors: false);

        var yaml = writer.ToString().Replace("\r\n", "\n").TrimEnd('\n');
        if (yaml.EndsWith("\n...", StringComparison.Ordinal))
        {
            yaml = yaml[..^4];
        }

        return yaml.TrimEnd('\n');
    }

    /// <summary>Plain scalars that YAML would read back as something other than a string.</summary>
    [GeneratedRegex(@"^(true|false|yes|no|on|off|y|n|null|~|[-+]?(\d[\d_]*(\.\d*)?|\.\d+)([eE][-+]?\d+)?|0x[0-9a-f]+|0o[0-7]+|[-+]?\.inf|\.nan)$", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeNonString();
}
