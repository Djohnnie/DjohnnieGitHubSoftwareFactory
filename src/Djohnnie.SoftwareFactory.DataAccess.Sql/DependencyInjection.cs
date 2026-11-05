using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

public static class DependencyInjection
{
    public static IServiceCollection AddSqlDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database must be configured.")));
        return services.AddSqlStores();
    }

    public static IServiceCollection AddSqlStores(this IServiceCollection services)
    {
        services.TryAddScoped<AiModelStore>();
        services.TryAddScoped<EpicStore>();
        services.TryAddScoped<AgentTemplateStore>();
        services.TryAddScoped<EpicTemplateStore>();
        services.TryAddScoped<RepositoryStore>();
        services.TryAddScoped<AgentStore>();
        return services;
    }

    public static async Task MigrateSqlDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
