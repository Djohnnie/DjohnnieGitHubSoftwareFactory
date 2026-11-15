using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Sync;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddSoftwareFactory(builder.Configuration);
builder.Services.AddSingleton<IGitHubTokenProvider>(sp => new ConfiguredGitHubTokenProvider(sp.GetRequiredService<IOptions<GitHubSyncOptions>>().Value.SyncToken));
builder.Services.AddOptions<GitHubSyncOptions>()
    .Bind(builder.Configuration.GetSection(GitHubSyncOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SyncToken), $"{GitHubSyncOptions.SectionName}:{nameof(GitHubSyncOptions.SyncToken)} must be configured.")
    .ValidateOnStart();

builder.Build().Run();
