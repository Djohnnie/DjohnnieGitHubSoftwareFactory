using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>
/// A fresh SQL Server database, created with the real migrations, for tests that need what the EF InMemory provider
/// doesn't do: unique indexes, restricted foreign keys, collation, transactions and concurrency checks.
/// Uses the server in <see cref="ServerVariable"/> (CI sets it to a SQL Server container), or LocalDB on Windows.
/// Tests are skipped when neither is available; with the variable set, a server that can't be reached fails them.
/// </summary>
public sealed class SqlServerDatabase : IAsyncDisposable
{
    /// <summary>A connection string without a database, e.g. <c>Server=localhost,1433;User ID=sa;Password=...;TrustServerCertificate=True</c>.</summary>
    public const string ServerVariable = "DGHSF_TEST_SQL_SERVER";

    private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True";

    private SqlServerDatabase(DbContextOptions<AppDbContext> options)
    {
        Options = options;
        Factory = new ModelPricingScraperTests.TestDbFactory(options);
    }

    public DbContextOptions<AppDbContext> Options { get; }

    public IDbContextFactory<AppDbContext> Factory { get; }

    public static async Task<SqlServerDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ServerVariable);
        var server = string.IsNullOrWhiteSpace(configured) ? OperatingSystem.IsWindows() ? LocalDb : null : configured;
        if (server is null)
        {
            Assert.Skip($"No SQL Server available: set {ServerVariable} to run the SQL Server tests.");
        }

        var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = $"dghsf-tests-{Guid.NewGuid():N}" }.ConnectionString;
        var database = new SqlServerDatabase(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
        try
        {
            await using var db = database.Factory.CreateDbContext();
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }
        catch (SqlException ex) when (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Skip($"LocalDB is not available ({ex.Message}); set {ServerVariable} to run the SQL Server tests.");
        }
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = Factory.CreateDbContext();
        await db.Database.EnsureDeletedAsync();
    }
}
