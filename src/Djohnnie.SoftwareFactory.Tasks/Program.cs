using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Tasks;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddSoftwareFactory(builder.Configuration);
builder.Services.AddSingleton<IGitHubTokenProvider>(sp => new ConfiguredGitHubTokenProvider(sp.GetRequiredService<IOptions<GitHubTasksOptions>>().Value.TasksToken));
builder.Services.AddOptions<GitHubTasksOptions>()
    .Bind(builder.Configuration.GetSection(GitHubTasksOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.TasksToken), $"{GitHubTasksOptions.SectionName}:{nameof(GitHubTasksOptions.TasksToken)} must be configured.")
    .ValidateOnStart();

builder.Build().Run();
