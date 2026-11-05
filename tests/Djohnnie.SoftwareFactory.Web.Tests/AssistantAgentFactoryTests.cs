using Djohnnie.SoftwareFactory.Agents;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ChatClient = OpenAI.Chat.ChatClient;
using OpenAI.Responses;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class AssistantAgentFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultClient_SendsFunctionToolsToResponses_AndReadsToolCalls(bool streaming)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        JsonElement request = default;
        app.MapPost("/openai/v1/responses", async (HttpContext context) =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            request = body.RootElement.Clone();
            var response = new
            {
                id = "resp_test",
                @object = "response",
                created_at = 1,
                status = "completed",
                model = "reasoning-deployment",
                output = new[]
                {
                    new
                    {
                        type = "function_call",
                        id = "fc_test",
                        call_id = "call_test",
                        name = "update_epic_form",
                        arguments = """{"title":"Add passkeys"}""",
                        status = "completed"
                    }
                }
            };
            if (streaming)
            {
                context.Response.ContentType = "text/event-stream";
                async Task SendEventAsync(string type, object data)
                {
                    await context.Response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data)}\n\n", context.RequestAborted);
                }

                await SendEventAsync("response.created", new { type = "response.created", sequence_number = 0, response });
                await SendEventAsync("response.output_item.added", new
                {
                    type = "response.output_item.added",
                    sequence_number = 1,
                    output_index = 0,
                    item = new { type = "function_call", id = "fc_test", call_id = "call_test", name = "update_epic_form", arguments = "", status = "in_progress" }
                });
                await SendEventAsync("response.function_call_arguments.delta", new
                {
                    type = "response.function_call_arguments.delta",
                    sequence_number = 2,
                    item_id = "fc_test",
                    output_index = 0,
                    delta = response.output[0].arguments
                });
                await SendEventAsync("response.function_call_arguments.done", new
                {
                    type = "response.function_call_arguments.done",
                    sequence_number = 3,
                    item_id = "fc_test",
                    output_index = 0,
                    arguments = response.output[0].arguments
                });
                await SendEventAsync("response.output_item.done", new
                {
                    type = "response.output_item.done",
                    sequence_number = 4,
                    output_index = 0,
                    item = response.output[0]
                });
                await SendEventAsync("response.completed", new { type = "response.completed", sequence_number = 5, response });
            }
            else
            {
                await context.Response.WriteAsJsonAsync(response, cancellationToken: context.RequestAborted);
            }
        });
        await app.StartAsync(timeout.Token);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = AssistantAgentFactory.CreateChatClient(new OpenAIOptions
            {
                Endpoint = $"{address}/openai/v1/",
                Model = "reasoning-deployment",
                ApiKey = "test-key"
            });
            var tool = AIFunctionFactory.Create((string title) => title, "update_epic_form");

            var messages = new[] { new ChatMessage(ChatRole.User, "Add passkeys") };
            var options = new ChatOptions { Tools = [tool] };
            var response = streaming
                ? await client!.GetStreamingResponseAsync(messages, options, timeout.Token).ToChatResponseAsync(timeout.Token)
                : await client!.GetResponseAsync(messages, options, timeout.Token);

            Assert.Equal("reasoning-deployment", request.GetProperty("model").GetString());
            var sentTool = Assert.Single(request.GetProperty("tools").EnumerateArray());
            Assert.Equal("function", sentTool.GetProperty("type").GetString());
            Assert.Equal("update_epic_form", sentTool.GetProperty("name").GetString());
            var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
            Assert.Equal("update_epic_form", call.Name);
            Assert.Equal("Add passkeys", call.Arguments!["title"]?.ToString());
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateChatClient_UsesResponsesForReasoningTools_UnlessExplicitlyDisabled(bool useResponsesApi)
    {
        using var client = AssistantAgentFactory.CreateChatClient(new OpenAIOptions
        {
            Endpoint = "https://example.openai.azure.com/",
            Model = "deployment",
            ApiKey = "test-key",
            UseResponsesApi = useResponsesApi
        });

#pragma warning disable OPENAI001
        Assert.Equal(useResponsesApi, client!.GetService<ResponsesClient>() is not null);
#pragma warning restore OPENAI001
        Assert.Equal(!useResponsesApi, client!.GetService<ChatClient>() is not null);
    }

    [Theory]
    [InlineData("https://example.openai.azure.com", "https://example.openai.azure.com/openai/v1/")]
    [InlineData(" https://example.openai.azure.com/ ", "https://example.openai.azure.com/openai/v1/")]
    [InlineData("https://example.services.ai.azure.com/", "https://example.services.ai.azure.com/openai/v1/")]
    [InlineData("https://example.openai.azure.com/openai/v1/", "https://example.openai.azure.com/openai/v1/")]
    [InlineData("https://example.openai.azure.com/custom/v1/", "https://example.openai.azure.com/custom/v1/")]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("https://example.openai.azure.com.example.com/", "https://example.openai.azure.com.example.com/")]
    [InlineData(null, "https://api.openai.com/v1")]
    public void CreateChatClient_UsesAzureV1ForResourceRoots_AndPreservesOtherEndpoints(string? endpoint, string expected)
    {
        using var client = AssistantAgentFactory.CreateChatClient(new OpenAIOptions
        {
            Endpoint = endpoint,
            Model = " deployment-name ",
            ApiKey = "test-key"
        });

        var metadata = client!.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal(expected, metadata.ProviderUri!.AbsoluteUri);
        Assert.Equal("deployment-name", metadata.DefaultModelId);
    }

    [Theory]
    [InlineData(null, "test-key")]
    [InlineData("deployment", null)]
    public void CreateChatClient_WithoutModelOrKey_RemainsUnconfigured(string? model, string? apiKey)
    {
        Assert.Null(AssistantAgentFactory.CreateChatClient(new OpenAIOptions { Model = model, ApiKey = apiKey }));
    }
}
