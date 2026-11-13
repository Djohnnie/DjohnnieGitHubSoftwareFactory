using System.Text;
using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Contracts.Skills;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

/// <summary>Writes a managed skill as the <c>SKILL.md</c> file that is committed to a repository.</summary>
public static partial class SkillFileSerializer
{
    private const string Delimiter = "---";

    /// <summary>The <c>SKILL.md</c> content: YAML frontmatter with the skill's properties, then its Markdown instructions.</summary>
    public static string Serialize(Skill skill)
    {
        var root = new YamlMappingNode();
        Add(root, "name", skill.Name);
        Add(root, "description", skill.Description);
        Add(root, "license", skill.License);
        Add(root, "compatibility", skill.Compatibility);
        Add(root, "allowed-tools", skill.AllowedTools);

        var builder = new StringBuilder();
        builder.Append(Delimiter).Append('\n');
        if (root.Children.Count > 0)
        {
            builder.Append(Emit(root)).Append('\n');
        }

        builder.Append(Delimiter).Append('\n');

        var instructions = skill.Instructions.Replace("\r\n", "\n").Trim('\n');
        if (instructions.Length > 0)
        {
            builder.Append('\n').Append(instructions).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Whether two files have the same content, ignoring line endings and trailing whitespace.</summary>
    public static bool AreEqual(string left, string right)
    {
        return Normalize(left) == Normalize(right);
    }

    private static string Normalize(string content)
    {
        return content.Replace("\r\n", "\n").TrimEnd();
    }

    private static void Add(YamlMappingNode mapping, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        value = value.Replace("\r\n", "\n").Trim();
        var style = value.Contains('\n') ? ScalarStyle.Literal
            : LooksLikeNonString().IsMatch(value) ? ScalarStyle.DoubleQuoted
            : ScalarStyle.Any;
        mapping.Add(new YamlScalarNode(key), new YamlScalarNode(value) { Style = style });
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
