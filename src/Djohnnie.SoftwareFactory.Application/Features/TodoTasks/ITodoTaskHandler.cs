using System.Text.Json;
using System.Text.Json.Serialization;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

/// <summary>
/// Executes the background tasks of one <see cref="TodoTaskKind"/> in the Tasks function app. A handler can run more than
/// once for the same task (a retry after an error or a crash), so it must not duplicate what an earlier attempt did.
/// Throw a <see cref="Contracts.DomainRuleException"/> for a problem a retry won't fix; other exceptions are retried.
/// </summary>
public interface ITodoTaskHandler
{
    TodoTaskKind Kind { get; }

    Task<TodoTaskOutcome> ExecuteAsync(TodoTask task, CancellationToken cancellationToken);
}

/// <summary>What a handler did, for people, and the sub-tasks it wants to run next.</summary>
public sealed record TodoTaskOutcome(string? Result, IReadOnlyList<NewTodoTask> SubTasks)
{
    public static TodoTaskOutcome Done(string? result)
    {
        return new(result, []);
    }
}

/// <summary>Reads and writes the JSON payloads of background tasks.</summary>
public static class TodoTaskPayloads
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write<T>(T payload)
    {
        return JsonSerializer.Serialize(payload, Options);
    }

    /// <exception cref="Contracts.DomainRuleException">The payload is not valid for <typeparamref name="T"/>; a retry won't fix that.</exception>
    public static T Read<T>(TodoTask task)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(task.Payload, Options)
                ?? throw new Contracts.DomainRuleException($"The task has no {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw new Contracts.DomainRuleException($"The task's payload is not a valid {typeof(T).Name}: {ex.Message}");
        }
    }
}
