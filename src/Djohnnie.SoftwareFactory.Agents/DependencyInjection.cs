using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAssistantAgents(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.SectionName));
        services.AddSingleton(sp => new AssistantAgentFactory(
            AssistantAgentFactory.CreateChatClient(sp.GetRequiredService<IOptions<OpenAIOptions>>().Value),
            sp.GetRequiredService<ILoggerFactory>()));
        return services;
    }
}
