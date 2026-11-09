using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Sync;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddSoftwareFactory(builder.Configuration);
builder.Services.AddSingleton<IGitHubTokenProvider, ConfiguredGitHubTokenProvider>();
builder.Services.AddOptions<GitHubSyncOptions>()
    .Bind(builder.Configuration.GetSection(GitHubSyncOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SyncToken), $"{GitHubSyncOptions.SectionName}:{nameof(GitHubSyncOptions.SyncToken)} must be configured.")
    .ValidateOnStart();

builder.Build().Run();
