using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.AgentRuns;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// One execution of the event-driven Container Apps job: the Service Bus scale rule starts it for a message on the queue; it
// runs that one agent run and exits.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSoftwareFactory(builder.Configuration);
builder.Services.AddOptions<AgentWorkerOptions>()
    .Bind(builder.Configuration.GetSection(AgentWorkerOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.GitHubToken), $"{AgentWorkerOptions.SectionName}:{nameof(AgentWorkerOptions.GitHubToken)} must be configured.")
    .ValidateOnStart();
builder.Services.AddSingleton<IGitHubTokenProvider>(sp => new ConfiguredGitHubTokenProvider(sp.GetRequiredService<IOptions<AgentWorkerOptions>>().Value.GitHubToken));
builder.Services.AddSingleton(_ => AgentRunQueue.CreateClient(builder.Configuration)
    ?? throw new InvalidOperationException($"Configure {AgentRunQueue.ConnectionName} (a connection string) or {AgentRunQueue.ConnectionName}__fullyQualifiedNamespace."));
builder.Services.AddSingleton<IGitWorkspace, GitWorkspace>();
builder.Services.AddSingleton<ICodingAgent, CopilotCodingAgent>();
builder.Services.AddScoped<AgentRunExecutor>();
builder.Services.AddScoped<AgentRunWorker>();

using var host = builder.Build();
await host.StartAsync();
// Stopping (SIGTERM at the replica timeout) cancels the run, which then gives it back or records how far it got.
var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
await using (var scope = host.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AgentRunWorker>().RunOnceAsync(stopping);
}
await host.StopAsync();
