using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Agents;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAssistantAgents(IConfiguration configuration)
        {
            services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.SectionName));
            services.Configure<GitHubMcpOptions>(configuration.GetSection(GitHubMcpOptions.SectionName));
            services.Configure<WebFetchOptions>(configuration.GetSection(WebFetchOptions.SectionName));
            services.AddSingleton(sp => AssistantAgentFactory.Create(
                sp.GetRequiredService<IOptions<OpenAIOptions>>().Value,
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<IOptions<WebFetchOptions>>().Value));
            return services;
        }
    }
}
