using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Pins a GitHub MCP tool to one repository. The <c>owner</c> and <c>repo</c> parameters are removed from the schema the
/// model sees and always set to the selected repository, and a search <c>query</c> loses its repo/org/user qualifiers
/// and gets <c>repo:owner/name</c>, so the model can't reach other repositories the user's token has access to.
/// </summary>
public sealed partial class RepositoryScopedFunction : DelegatingAIFunction
{
    public const string OwnerParameter = "owner";
    public const string RepoParameter = "repo";
    public const string QueryParameter = "query";

    private readonly string _owner;
    private readonly string _repo;
    private readonly JsonElement _schema;

    public RepositoryScopedFunction(AIFunction innerFunction, string repositoryFullName)
        : base(innerFunction)
    {
        var parts = repositoryFullName.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new ArgumentException($"'{repositoryFullName}' is not an owner/name repository.", nameof(repositoryFullName));
        }

        _owner = parts[0];
        _repo = parts[1];
        _schema = WithoutRepositoryParameters(innerFunction.JsonSchema);
    }

    public string RepositoryFullName
    {
        get
        {
            return $"{_owner}/{_repo}";
        }
    }

    public override JsonElement JsonSchema
    {
        get
        {
            return _schema;
        }
    }

    /// <summary><c>true</c> for a search tool whose <c>query</c> is pinned to the repository with a qualifier.</summary>
    public bool ScopesQuery
    {
        get
        {
            return Name.StartsWith("search_", StringComparison.Ordinal) && HasProperty(InnerFunction.JsonSchema, QueryParameter);
        }
    }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var scoped = new AIFunctionArguments(arguments.ToDictionary(a => a.Key, a => a.Value))
        {
            Services = arguments.Services,
            Context = arguments.Context
        };

        if (HasProperty(InnerFunction.JsonSchema, OwnerParameter))
        {
            scoped[OwnerParameter] = _owner;
        }
        if (HasProperty(InnerFunction.JsonSchema, RepoParameter))
        {
            scoped[RepoParameter] = _repo;
        }
        if (ScopesQuery)
        {
            scoped[QueryParameter] = ScopeQuery(AsString(scoped.GetValueOrDefault(QueryParameter)));
        }

        return base.InvokeCoreAsync(scoped, cancellationToken);
    }

    /// <summary>Removes the repo, org, user and owner qualifiers from a search query and pins it to the repository.</summary>
    public string ScopeQuery(string? query)
    {
        var rest = ScopeQualifier().Replace(query ?? string.Empty, " ");
        rest = Whitespace().Replace(rest, " ").Trim();
        return rest.Length == 0 ? $"repo:{RepositoryFullName}" : $"repo:{RepositoryFullName} {rest}";
    }

    /// <summary><c>true</c> when a tool takes both an <c>owner</c> and a <c>repo</c>, so it can be pinned to one repository.</summary>
    public static bool TakesRepository(JsonElement schema)
    {
        return HasProperty(schema, OwnerParameter) && HasProperty(schema, RepoParameter);
    }

    private static bool HasProperty(JsonElement schema, string name)
    {
        return schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty(name, out _);
    }

    private static JsonElement WithoutRepositoryParameters(JsonElement schema)
    {
        if (JsonNode.Parse(schema.GetRawText()) is not JsonObject root)
        {
            return schema;
        }

        if (root["properties"] is JsonObject properties)
        {
            properties.Remove(OwnerParameter);
            properties.Remove(RepoParameter);
        }
        if (root["required"] is JsonArray required)
        {
            foreach (var item in required.Where(r => r?.GetValue<string>() is OwnerParameter or RepoParameter).ToList())
            {
                required.Remove(item);
            }
        }

        return JsonSerializer.SerializeToElement(root);
    }

    private static string? AsString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => element.GetRawText(),
            _ => value.ToString()
        };
    }

    [GeneratedRegex(@"(?<!\S)-?(repo|org|user|owner)\s*:\s*(""[^""]*""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ScopeQualifier();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
