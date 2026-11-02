using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace Djohnnie.SoftwareFactory.Web.Auth;

public sealed class MemoryCacheTicketStore(IMemoryCache cache) : ITicketStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        Set(key, ticket);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        Set(key, ticket);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        Task.FromResult(cache.TryGetValue(key, out AuthenticationTicket? ticket) ? ticket : null);

    public Task RemoveAsync(string key)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }

    private void Set(string key, AuthenticationTicket ticket) =>
        cache.Set(key, ticket, new MemoryCacheEntryOptions { SlidingExpiration = Lifetime });
}
