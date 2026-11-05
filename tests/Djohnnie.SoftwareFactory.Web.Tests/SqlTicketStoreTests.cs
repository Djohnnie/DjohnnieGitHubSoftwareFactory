using System.Security.Claims;
using System.Text;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class SqlTicketStoreTests : IDisposable
{
    private const string Token = "gho_secret-token";
    private readonly ServiceProvider _services;
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public SqlTicketStoreTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(_dbOptions))
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    [Fact]
    public async Task StoredTickets_SurviveANewStoreInstance_AndAreEncryptedAtRest()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var key = await Store(dataProtection).StoreAsync(Ticket());

        // A new instance (like after a restart) with the same data protection keys reads it back.
        var restored = await Store(dataProtection).RetrieveAsync(key);

        Assert.Matches("^[0-9A-F]{64}$", key);
        Assert.Equal("Djohnnie", restored!.Principal.Identity!.Name);
        Assert.Equal(Token, restored.Principal.FindFirst(GitHubClaims.AccessToken)!.Value);
        await using var db = new AppDbContext(_dbOptions);
        var stored = Assert.Single(await db.AuthSessions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Token, Encoding.UTF8.GetString(stored.Ticket));
    }

    [Fact]
    public async Task RemovedOrUnreadableTickets_AreSignedOut()
    {
        var store = Store(new EphemeralDataProtectionProvider());
        var key = await store.StoreAsync(Ticket());

        // Other data protection keys can't decrypt it: treated as signed out instead of failing the request.
        Assert.Null(await Store(new EphemeralDataProtectionProvider()).RetrieveAsync(key));

        await store.RemoveAsync(key);
        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task RenewedTickets_ReplaceTheStoredTicket()
    {
        var store = Store(new EphemeralDataProtectionProvider());
        var key = await store.StoreAsync(Ticket());
        var renewed = Ticket();
        renewed.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8);
        renewed.Properties.Items["renewed"] = "yes";

        await store.RenewAsync(key, renewed);

        Assert.Equal("yes", (await store.RetrieveAsync(key))!.Properties.Items["renewed"]);
    }

    private SqlTicketStore Store(IDataProtectionProvider dataProtection)
    {
        return new SqlTicketStore(_services.GetRequiredService<IServiceScopeFactory>(), dataProtection, TimeProvider.System,
            NullLogger<SqlTicketStore>.Instance);
    }

    private static AuthenticationTicket Ticket()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Djohnnie"), new Claim(GitHubClaims.AccessToken, Token)], "GitHub");
        return new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties(), "Cookies");
    }
}
