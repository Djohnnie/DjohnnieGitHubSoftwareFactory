using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;
using Djohnnie.SoftwareFactory.Application.Features.AiModels;
using Djohnnie.SoftwareFactory.Application.Features.AuthSessions;
using Djohnnie.SoftwareFactory.Application.Features.Assistant;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.Application.Features.Uploads;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Djohnnie.SoftwareFactory.Application;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApplication(IConfiguration? configuration = null)
        {
            services.AddMediatR(options =>
            {
                options.LicenseKey = configuration?["MediatR:LicenseKey"];
                options.RegisterServicesFromAssemblyContaining<WorkItemService>();
                options.AddOpenBehavior(typeof(ValidationBehavior<,>));
            });
            services.AddValidatorsFromAssemblyContaining<WorkItemService>();
            services.TryAddSingleton(TimeProvider.System);
            services.AddSqlStores();
            services.AddScoped<WorkItemWorkflow>();
            services.AddScoped<AgentTemplateWorkflow>();
            services.AddScoped<AiModelService>();
            services.AddScoped<WorkItemService>();
            services.AddScoped<AgentTemplateService>();
            services.AddScoped<WorkItemTemplateService>();
            services.AddScoped<GitHubService>();
            services.AddScoped<RepositoryService>();
            services.AddScoped<AuthSessionService>();
            services.AddScoped<AgentService>();
            services.AddScoped<AssistantService>();
            services.AddScoped<InternalAgentRegistry>();
            services.AddScoped<InternalAgentService>();
            services.AddScoped<WorkItemModelSelector>();
            // AddAssistantAgents replaces this unconfigured factory with the one for the OpenAI settings.
            services.TryAddSingleton(sp => new AssistantAgentFactory(null, sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance));
            services.TryAddSingleton<BackgroundAgents>();
            services.AddScoped<ImageUploadService>();
            var imageUploads = services.AddOptions<ImageUploadOptions>();
            if (configuration is not null)
            {
                imageUploads.Bind(configuration.GetSection(ImageUploadOptions.SectionName));
            }

            services.TryAddSingleton<IImageStorage, AzureBlobImageStorage>();
            services.AddHttpClient<ModelPricingScraper>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DGHSF/1.0");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddHttpClient<GitHubApiClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.github.com/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DGHSF/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            });
            return services;
        }

        public IServiceCollection AddSoftwareFactory(IConfiguration configuration)
        {
            return services.AddApplication(configuration).AddSqlDataAccess(configuration).AddAssistantAgents(configuration);
        }
    }

    extension(IServiceProvider services)
    {
        public Task MigrateApplicationDatabaseAsync(CancellationToken cancellationToken = default)
        {
            return services.MigrateSqlDatabaseAsync(cancellationToken);
        }

        /// <summary>Adds the data the application needs that is missing, such as the internal agents with their default prompts.</summary>
        public async Task SeedApplicationDataAsync(CancellationToken cancellationToken = default)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<InternalAgentService>().SeedAsync(cancellationToken);
        }
    }
}
