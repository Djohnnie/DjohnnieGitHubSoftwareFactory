using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.Agents;
using Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;
using Djohnnie.SoftwareFactory.Application.Features.AiModels;
using Djohnnie.SoftwareFactory.Application.Features.Assistant;
using Djohnnie.SoftwareFactory.Application.Features.Epics;
using Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.Repositories;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Djohnnie.SoftwareFactory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddMediatR(options =>
        {
            options.LicenseKey = configuration?["MediatR:LicenseKey"];
            options.RegisterServicesFromAssemblyContaining<EpicService>();
            options.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssemblyContaining<EpicService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSqlStores();
        services.AddScoped<EpicWorkflow>();
        services.AddScoped<AgentTemplateWorkflow>();
        services.AddScoped<AiModelService>();
        services.AddScoped<EpicService>();
        services.AddScoped<AgentTemplateService>();
        services.AddScoped<EpicTemplateService>();
        services.AddScoped<GitHubService>();
        services.AddScoped<RepositoryService>();
        services.AddScoped<AgentService>();
        services.AddScoped<AssistantService>();
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

    public static IServiceCollection AddSoftwareFactory(this IServiceCollection services, IConfiguration configuration) =>
        services.AddApplication(configuration).AddSqlDataAccess(configuration).AddAssistantAgents(configuration);

    public static Task MigrateApplicationDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default) =>
        services.MigrateSqlDatabaseAsync(cancellationToken);
}
