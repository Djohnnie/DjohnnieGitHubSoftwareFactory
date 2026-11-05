using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSqlDataAccess(IConfiguration configuration)
        {
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("Database")
                    ?? throw new InvalidOperationException("ConnectionStrings:Database must be configured.")));
            return services.AddSqlStores();
        }

        public IServiceCollection AddSqlStores()
        {
            services.TryAddScoped<IAiModelStore, AiModelStore>();
            services.TryAddScoped<IWorkItemStore, WorkItemStore>();
            services.TryAddScoped<IAgentTemplateStore, AgentTemplateStore>();
            services.TryAddScoped<IWorkItemTemplateStore, WorkItemTemplateStore>();
            services.TryAddScoped<IRepositoryStore, RepositoryStore>();
            services.TryAddScoped<IAgentStore, AgentStore>();
            return services;
        }
    }

    extension(IServiceProvider services)
    {
        public async Task MigrateSqlDatabaseAsync(CancellationToken cancellationToken = default)
        {
            await using var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }
    }
}
